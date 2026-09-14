using System.Globalization;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// Compiles a stylesheet tree into the instruction and pattern representation the engine executes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stylesheet is itself loaded as an <see cref="XdmTree"/>. Beyond avoiding a second parser, that gives
    /// the compiler the per-element namespace scopes it needs: the prefixes in an XPath expression must be
    /// resolved as they were bound at the exact element the expression was written on, which is exactly what
    /// <see cref="XdmTree.ResolvePrefix"/> reports.
    /// </para>
    /// <para>
    /// Compilation runs in two passes. The first declares every template and global variable so that names
    /// exist before anything refers to them; the second compiles the bodies. Without that split, a
    /// <c>call-template</c> could not refer to a template declared later in the file.
    /// </para>
    /// </remarks>
    internal sealed class StylesheetCompiler : IXPathStaticContext, ISchemaTypeProvider
    {
        /// <summary>The XSLT namespace.</summary>
        public const string XsltNamespace = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>
        /// The module currently being read.
        /// </summary>
        /// <remarks>
        /// Held as state rather than passed around because every lookup the compiler performs — resolving a
        /// prefix, reading an attribute, walking children — is relative to the module a declaration was written
        /// in. Declarations record their own module so the second pass can restore it before compiling them.
        /// </remarks>
        private XdmTree m_tree;
        private readonly NameSlotTable m_names = new();
        private readonly List<TemplateRule> m_rules = new();
        private readonly List<GlobalVariable> m_globals = new();
        private readonly Dictionary<ExpandedName, Template> m_namedTemplates = new();
        private readonly Dictionary<ExpandedName, int> m_modes = new();

        /// <summary>Whether any module read so far writes an attribute in the shadow form.</summary>
        private bool m_hasShadowAttributes;

        /// <summary>
        /// How many <c>_version</c> shadow attributes are being evaluated, which is when the version in
        /// force cannot be read from the element: the expression computing it has to be parsed first.
        /// </summary>
        private int m_shadowVersionDepth;

        /// <summary>The accumulators declared, in the order their values are addressed at run time.</summary>
        private readonly List<AccumulatorDefinition> m_accumulators = new();

        /// <summary>The same, by name, which is what an accumulator-before() call resolves against.</summary>
        private readonly Dictionary<ExpandedName, AccumulatorDefinition> m_accumulatorsByName = new();

        /// <summary>The import precedence each accumulator was last declared at.</summary>
        private readonly Dictionary<ExpandedName, int> m_accumulatorPrecedence = new();

        /// <summary>Each accumulator name declared twice at one precedence, with that precedence.</summary>
        private readonly Dictionary<ExpandedName, (int Precedence, string Name)> m_accumulatorClashes = new();

        /// <summary>Accumulators whose rules are not compiled yet; see <see cref="CompileAccumulators"/>.</summary>
        private readonly List<(AccumulatorDefinition Accumulator, ModuleElement Source)> m_pendingAccumulators
            = new();

        /// <summary>Whether the principal module is an xsl:package rather than an xsl:stylesheet.</summary>
        /// <remarks>
        /// What makes visibility mean anything: a package has an outside, and a stylesheet does not.
        /// </remarks>
        private bool m_isPackage;

        /// <summary>Every component of every package read, for an xsl:expose to name.</summary>
        private readonly List<PackageComponent> m_components = new();

        /// <summary>The xsl:expose declarations, kept until every component is known.</summary>
        /// <remarks>
        /// An xsl:expose may stand anywhere among the top-level declarations and names components declared
        /// on either side of it, so there is nothing to check until the last module has been read.
        /// </remarks>
        private readonly List<(ModuleElement Source, int Package)> m_exposures = new();

        /// <summary>Which package the module being read belongs to.</summary>
        /// <remarks>
        /// Modules are flattened into one compilation, which is right for everything except this: an
        /// xsl:expose is about its own package and no other. A used package saying
        /// <c>names="*" visibility="public"</c> is describing what it offers, and letting that reach the
        /// package that used it made a private template there public — which is the whole of what
        /// XTDE0040 in one of the tests is about. An import or an include stays inside the package it was
        /// written into, so only xsl:use-package moves this on.
        /// </remarks>
        private int m_package;

        /// <summary>How many packages have been read, for the next one's number.</summary>
        private int m_packageCount;

        /// <summary>The xsl:accept declarations, each with the package its xsl:use-package brought in.</summary>
        private readonly List<Acceptance> m_acceptances = new();

        /// <summary>The package each module was read as part of, so that a body knows whose it is.</summary>
        private readonly Dictionary<XdmTree, int> m_packageOfModule = new();

        /// <summary>Which packages each package uses, directly, in the order it named them.</summary>
        private readonly Dictionary<int, List<int>> m_uses = new();

        /// <summary>The package each <c>xsl:use-package</c> brought in, by the element that named it.</summary>
        private readonly Dictionary<ModuleElement, int> m_usePackageIds = new();

        /// <summary>Every declaration spliced in from an <c>xsl:override</c>, and the <c>xsl:use-package</c> it stood in.</summary>
        private readonly Dictionary<ModuleElement, ModuleElement> m_overrideOf = new();

        /// <summary>The slot an overriding global's original moved to, by the slot the override took over.</summary>
        private readonly Dictionary<int, int> m_originalSlots = new();

        /// <summary>The slot of the global whose value is being compiled, or -1 outside one.</summary>
        private int m_compilingGlobalSlot = -1;

        /// <summary>The function whose body is being compiled, for what xsl:original means in it.</summary>
        private UserFunction? m_compilingFunction;

        /// <summary>The key an overriding attribute set's original was kept under, for xsl:original.</summary>
        private readonly Dictionary<AttributeSetDeclaration, ExpandedName> m_originalAttributeSets = new();

        /// <summary>The modes a package put template rules in without declaring, one entry per mode.</summary>
        private readonly List<(int Package, int Mode, ModuleElement Source)> m_implicitModes = new();

        /// <summary>How many xsl:imports deep the module being loaded was reached.</summary>
        private int m_importDepth;

        /// <summary>The components an <c>xsl:override</c> declared, by package, for the clash checks.</summary>
        private readonly HashSet<(int Package, string Kind, ExpandedName Name, int Arity)> m_overridingKeys = new();

        /// <summary>Each <c>xsl:accept</c> as read, so that asking what a package sees does not re-read it.</summary>
        private readonly Dictionary<Acceptance, Exposure> m_readAcceptances = new();

        /// <summary>The template whose body is being compiled, which is what xsl:original is relative to.</summary>
        private Template? m_compilingTemplate;

        /// <summary>The name an xsl:override uses to reach the component it replaced.</summary>
        private static readonly ExpandedName XsltOriginal =
            new ExpandedName("http://www.w3.org/1999/XSL/Transform", "original");

        /// <summary>The packages already brought in, so that naming one twice does not read it twice.</summary>
        private readonly Dictionary<string, int> m_packageIdByName = new(StringComparer.Ordinal);

        /// <summary>What xsl:global-context-item says the transformation is being run against.</summary>
        private ContextItemDeclaration m_globalContextItem = ContextItemDeclaration.Default;

        /// <summary>Every xsl:global-context-item read, with the package and module it was written in.</summary>
        private readonly List<(int Package, XdmTree Module, ContextItemDeclaration Declared)>
            m_globalContextItems = new();

        /// <summary>What <c>xsl:mode</c> said about each mode, with the precedence that said it.</summary>
        private readonly Dictionary<int, ModeStatement> m_modeDeclarations = new();
        private readonly List<VariableBinding> m_scope = new();
        private readonly List<(Template Template, ModuleElement Source)> m_pendingTemplates = new();

        private readonly List<KeyDefinition> m_keys = new();
        private readonly List<ModuleElement> m_globalElements = new();
        private readonly OutputSettings m_outputSettings = new();
        private readonly Dictionary<ExpandedName, DecimalFormat> m_decimalFormats = new();
        private readonly Dictionary<ExpandedName, AttributeSet> m_attributeSets = new();

        /// <summary>Every name a use-attribute-sets drew in, checked once every declaration is known.</summary>
        private readonly List<(ExpandedName Name, string Written, int Package)> m_usedAttributeSets = new();

        /// <summary>Every global declared, with the import precedence it was declared at.</summary>
        private readonly HashSet<(ExpandedName Name, int Precedence)> m_declaredGlobals = new();

        /// <summary>What each xsl:output attribute was given, per definition and import precedence.</summary>
        private readonly Dictionary<(ExpandedName Definition, int Precedence, string Attribute), string>
            m_outputAttributes = new();

        /// <summary>What each xsl:decimal-format property was last given, and by which declaration.</summary>
        private readonly Dictionary<(ExpandedName Format, string Property), DecimalFormatProperty>
            m_decimalFormatProperties = new();
        private readonly Dictionary<string, NamespaceAlias> m_namespaceAliases = new(StringComparer.Ordinal);
        private readonly List<(AttributeSetDeclaration Declaration, ModuleElement Source)> m_pendingAttributeSets = new();

        /// <summary>
        /// The <c>xsl:character-map</c> declarations, and the ones already expanded into their substitutions.
        /// </summary>
        /// <remarks>
        /// Expansion waits until every module is read, because <c>use-character-maps</c> may name a map
        /// declared later in the stylesheet or in a module not yet loaded.
        /// </remarks>
        /// <summary>
        /// Every <c>xsl:call-template</c>, kept so that its parameters can be checked against the template it
        /// names once that template's own declarations are known.
        /// </summary>
        private readonly List<(Template Target, WithParameter[] Supplied, string Name, bool Legacy)>
            m_pendingCalls = new();

        /// <summary>Templates declared <c>mode="#all"</c>, which are given their rules once every mode is known.</summary>
        private readonly List<(Template Template, Pattern[] Patterns, double? Priority)> m_everyModeTemplates = new();

        private readonly Dictionary<ExpandedName, CharacterMapDeclaration> m_characterMaps = new();
        private readonly Dictionary<ExpandedName, Dictionary<int, string>> m_expandedCharacterMaps = new();
        private readonly List<ModuleElement> m_outputCharacterMaps = new();

        /// <summary>
        /// The <c>xsl:output</c> declarations that carry a name, kept as elements rather than as settings.
        /// </summary>
        /// <remarks>
        /// Read only when an <c>xsl:result-document</c> asks for one, which is in the second pass — a named
        /// output may name a character map declared further down the stylesheet, and reading it where it
        /// stands would look for one that is not there yet. Several declarations may share a name and are
        /// merged in the order they were read.
        /// </remarks>
        private readonly Dictionary<ExpandedName, List<ModuleElement>> m_namedOutputs = new();

        /// <summary>
        /// The serialization attributes of the xsl:result-document being compiled that are attribute value
        /// templates, and so are read when the instruction runs rather than here.
        /// </summary>
        private readonly HashSet<string> m_templatedOutput = new(StringComparer.Ordinal);

        /// <summary>A serialization attribute, or null where it is a template to be read later.</summary>
        private string? OutputAttribute(int element, string name)
        {
            return m_templatedOutput.Contains(name) ? null : GetAttribute(element, name);
        }

        /// <summary>What <c>use-when</c> answered for each element that carries one.</summary>
        private readonly Dictionary<(XdmTree Tree, int Element), bool> m_useWhen = new();

        /// <summary>The base URI of the principal module's outermost element, once it has been seen.</summary>
        private string? m_stylesheetBaseUri;

        /// <summary>The URI each module was read from, which is the base URI of what it holds.</summary>
        private readonly Dictionary<XdmTree, string?> m_moduleUris = new();

        /// <summary>Whether a <c>use-when</c> expression is being compiled now.</summary>
        /// <remarks>
        /// Nothing the stylesheet declares is in scope in one: a <c>use-when</c> decides whether a piece of
        /// the stylesheet exists at all, and it is answered before the rest of the stylesheet has been read,
        /// so a variable or a function it named could itself be inside a piece another <c>use-when</c>
        /// removed.
        /// </remarks>
        private bool m_inUseWhen;

        /// <summary>
        /// What each <c>static</c> variable or parameter was settled at, by name.
        /// </summary>
        /// <remarks>
        /// Filled as the top-level declarations are read rather than in the compiling pass that follows,
        /// because a <c>use-when</c> further down the same module has to be able to ask about one — and a
        /// <c>use-when</c> is answered as the declaration carrying it is first reached. Reading order is
        /// therefore the whole of the scoping rule here: a static variable can name only the static
        /// variables declared before it, which falls out of the table simply not holding the later ones yet.
        /// </remarks>
        private readonly Dictionary<ExpandedName, XPathValue> m_staticValues = new();

        /// <summary>
        /// Functions declared by <c>xsl:function</c>, keyed by name and arity together — two functions of one
        /// name taking different numbers of arguments are two functions.
        /// </summary>
        private readonly Dictionary<(ExpandedName Name, int Arity), UserFunction> m_functions = new();

        /// <summary>The import precedence each declared function was last declared at.</summary>
        private readonly Dictionary<(ExpandedName Name, int Arity), int> m_functionPrecedence = new();
        private readonly List<(UserFunction Function, ModuleElement Source)> m_pendingFunctions = new();

        /// <summary>
        /// What each package strips, kept apart because whitespace stripping is local to a package.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 §3.6.5: an <c>xsl:strip-space</c> or <c>xsl:preserve-space</c> in a library package
        /// affects only the <c>doc()</c> and <c>document()</c> calls written in that package, and one in
        /// the top-level package additionally strips the source document. So a library that says nothing
        /// preserves everything, whatever the package using it declares, and one that says something does
        /// not impose it on anybody else.
        /// </remarks>
        private readonly Dictionary<int, WhitespaceControl> m_whitespaceByPackage = new();

        /// <summary>The whitespace control a package declares into, made on demand.</summary>
        /// <param name="package">The package.</param>
        private WhitespaceControl WhitespaceOf(int package)
        {
            if (!m_whitespaceByPackage.TryGetValue(package, out WhitespaceControl? control))
            {
                m_whitespaceByPackage[package] = control = new WhitespaceControl();
            }

            return control;
        }

        private int m_scopeElement;
        private int m_globalSlotCount;
        private int m_frameSlotCount;
        private OutputMethod m_outputMethod = OutputMethod.Xml;

        private readonly XsltOptions m_options;
        private readonly XsltBackend m_backend;
        private int m_nextPrecedence;

        /// <summary>
        /// The lowest precedence the module being read imported, which with its own precedence bounds
        /// what an xsl:apply-imports written in it may reach.
        /// </summary>
        private int m_importFloor;

        /// <summary>
        /// The schema components in scope, or null for a processor that is not schema-aware. Made with the
        /// caller's schemas, and grown by every xsl:import-schema.
        /// </summary>
        private readonly SchemaComponents? m_schemas;

        private StylesheetCompiler(XdmTree tree, XsltOptions options)
        {
            m_tree = tree;
            m_options = options;
            m_backend = options.Backend;
            m_schemas = options.SchemaAware ? new SchemaComponents(options.Schemas, options.SchemaResolver) : null;
        }

        /// <summary>
        /// What the modules read so far say about the type annotations of the documents the stylesheet
        /// reads: true for <c>strip</c>, false for <c>preserve</c>, null while none has said either.
        /// </summary>
        private bool? m_stripInputTypeAnnotations;

        /// <summary>
        /// Records a module's <c>input-type-annotations</c>. <c>unspecified</c> says nothing, and this
        /// engine then preserves; <c>strip</c> and <c>preserve</c> from two modules of one stylesheet
        /// cannot both be honoured (§4.4, <c>XTSE0265</c>).
        /// </summary>
        /// <param name="said">The attribute's value.</param>
        private void NoteInputTypeAnnotations(string said)
        {
            bool? asked = said.Trim() switch
            {
                "strip" => true,
                "preserve" => false,
                _ => null,
            };

            if (asked is null)
            {
                return;
            }

            if (m_stripInputTypeAnnotations is bool settled && settled != asked)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0265,
                    "One stylesheet module says input-type-annotations=\"strip\" and another says "
                    + "\"preserve\", and the documents read cannot be both.");
            }

            m_stripInputTypeAnnotations = asked;
        }

        /// <inheritdoc/>
        /// <remarks>What an expression's type name resolves to beyond the built-in types: the imported schemas' types.</remarks>
        public XdmSchemaType? ResolveSchemaType(string namespaceUri, string localName)
        {
            return m_schemas?.FindType(namespaceUri, localName);
        }

        /// <inheritdoc/>
        public XdmSchemaDeclaration? ResolveElementDeclaration(string namespaceUri, string localName)
        {
            return m_schemas?.FindElement(namespaceUri, localName);
        }

        /// <inheritdoc/>
        public XdmSchemaDeclaration? ResolveAttributeDeclaration(string namespaceUri, string localName)
        {
            return m_schemas?.FindAttribute(namespaceUri, localName);
        }

        /// <summary>Compiles a stylesheet.</summary>
        /// <param name="tree">The stylesheet, already parsed into a tree.</param>
        /// <param name="options">Configuration, including how expressions execute and how references resolve.</param>
        /// <returns>The compiled stylesheet.</returns>
        /// <exception cref="XsltException">The stylesheet is not valid.</exception>
        public static CompiledStylesheet Compile(XdmTree tree, XsltOptions? options = null)
        {
            StylesheetCompiler compiler = new StylesheetCompiler(tree, options ?? XsltOptions.Default);
            return compiler.Run();
        }

        /// <summary>One top-level declaration, remembered with the module it was written in.</summary>
        /// <param name="Tree">The module the declaration was written in.</param>
        /// <param name="Element">The declaring element.</param>
        /// <param name="Simplified">
        /// Whether this is a simplified stylesheet's document element, standing for a template matching
        /// <c>/</c> whose body is the element itself.
        /// </param>
        private readonly record struct ModuleElement(XdmTree Tree, int Element, bool Simplified = false);

        /// <inheritdoc/>
        public NameSlotTable Names => m_names;

        /// <inheritdoc/>
        public XsltVersion Version => VersionOf(m_scopeElement);

        /// <inheritdoc/>
        public XsltVersion SyntaxVersion => m_options.Version;

        /// <inheritdoc/>
        /// <remarks>
        /// Never, whatever the stylesheet's version says. This is a 2.0 processor, and XSLT 2.0 is explicit
        /// that a 1.0 stylesheet running on one is still reading XPath 2.0 expressions — with backwards
        /// compatible behaviour switched on, which changes how the values are read and not what may be
        /// written. So <c>1 to 5</c> and <c>()</c> and <c>current-date()</c> are all available to a
        /// <c>version="1.0"</c> stylesheet, and <see cref="Version"/> settles what they then mean.
        /// </remarks>
        public bool LegacySyntax => false;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> InScopeNamespaces => NamespacesOn(m_scopeElement);

        /// <inheritdoc/>
        /// <remarks>
        /// Cached against the element it was worked out for: every expression written at one element has
        /// the same answer, and working it out means a walk up the tree and a resolution apiece.
        /// </remarks>
        public string DefaultCollation
        {
            get
            {
                if (m_collationElement != m_scopeElement)
                {
                    m_collationChosen = ChosenCollation(m_scopeElement);
                    m_collationElement = m_scopeElement;
                }

                return m_collationChosen;
            }
        }

        /// <inheritdoc/>
        /// <remarks>The caller's, from the options the stylesheet is compiled with.</remarks>
        public IXsltCollationResolver? CollationResolver => m_options.CollationResolver;

        /// <summary>The element <see cref="m_collationChosen"/> was worked out for.</summary>
        private int m_collationElement = -1;

        /// <summary>The collation in scope at that element, as a URI.</summary>
        private string m_collationChosen = Collation.CodepointUri;

        /// <summary>
        /// The collation a <c>default-collation</c> in scope at an element puts there.
        /// </summary>
        /// <remarks>
        /// Inherited down the stylesheet tree as <c>version</c> and <c>expand-text</c> are, so the nearest
        /// ancestor that says anything decides. The value is a list of candidates, most preferred first, and
        /// the first one this engine has is the one taken — a stylesheet may name a collation it would like
        /// and one it can settle for. That none of them is recognised has already been refused where the
        /// attribute was read.
        /// </remarks>
        /// <param name="element">The element the expression is written at.</param>
        private string ChosenCollation(int element)
        {
            for (int current = element; current >= 0; current = m_tree.ParentOf(current))
            {
                if (m_tree.KindOf(current) != NodeKind.Element)
                {
                    continue;
                }

                string? said = (IsXsltElement(current, out _)
                    ? GetAttribute(current, "default-collation")
                    : null) ?? GetXsltAttribute(current, "default-collation");

                if (said is null)
                {
                    continue;
                }

                foreach (Range candidate in said.AsSpan().Trim().Split(' '))
                {
                    string uri = said.AsSpan().Trim()[candidate].ToString();

                    if (uri.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        Collation.Resolve(uri, m_options.CollationResolver);
                        return uri;
                    }
                    catch (XsltException)
                    {
                        // Try the next candidate: the list is what a stylesheet will settle for, in order.
                    }
                }
            }

            return Collation.CodepointUri;
        }

        /// <inheritdoc/>
        public string? ResolvePrefix(string prefix)
        {
            // XPath 2.0 binds xs, fn and the rest without being asked, and XSLT does not: a stylesheet's
            // statically known namespaces are the in-scope namespaces of the element the expression is
            // written in and nothing besides (XSLT 3.0 §5.4.1). A prefix nobody declared is unbound, which
            // is XPST0081 where it is used — a stylesheet writing fn:current-dateTime() having declared no
            // fn is told so rather than quietly given the library it happened to mean.
            return m_tree.ResolvePrefix(m_scopeElement, prefix);
        }

        /// <inheritdoc/>
        public bool TryResolveVariable(string namespaceUri, string localName, out int slot, out bool isGlobal)
        {
            ExpandedName name = new ExpandedName(namespaceUri, localName);

            if (m_inUseWhen)
            {
                slot = 0;
                isGlobal = false;
                return false;
            }

            // $xsl:original is not a name in any package's space: from the value of an overriding global it
            // means the one overridden, which moved to a slot of its own when the override took over its.
            if (name.Equals(XsltOriginal))
            {
                if (m_compilingGlobalSlot >= 0 && m_originalSlots.TryGetValue(m_compilingGlobalSlot, out slot))
                {
                    isGlobal = true;
                    return true;
                }

                slot = 0;
                isGlobal = false;
                return false;
            }

            // Innermost declaration wins, so search the scope stack from the top.
            for (int i = m_scope.Count - 1; i >= 0; i--)
            {
                if (m_scope[i].Name.Equals(name))
                {
                    slot = m_scope[i].Slot;
                    isGlobal = m_scope[i].IsGlobal;

                    // A global variable is not in scope within its own declaration (XSLT 3.0 §9.7). The
                    // reference is to a variable that has not been declared where it stands, which is
                    // XPST0008 — so an inline function bound to a global cannot recurse by naming the
                    // global it is being bound to, however much sense that would make.
                    if (isGlobal && slot == m_compilingGlobalSlot)
                    {
                        slot = 0;
                        isGlobal = false;
                        return false;
                    }

                    // A global another package declared and did not offer is, from here, not declared at
                    // all — which is what the parser then says of it.
                    if (isGlobal && !GlobalIsVisible(name, slot))
                    {
                        slot = 0;
                        isGlobal = false;
                        return false;
                    }

                    return true;
                }
            }

            slot = 0;
            isGlobal = false;
            return false;
        }

        private CompiledStylesheet Run()
        {
            XdmTree principal = m_tree;
            HashSet<string> loading = new(StringComparer.OrdinalIgnoreCase);
            if (m_options.BaseUri is not null)
            {
                loading.Add(m_options.BaseUri);
            }

            // Static variables first, in stylesheet tree order with every import and include in place, so
            // that one declared in an imported module is worth something to a declaration, a use-when or a
            // shadow attribute that follows the import.
            if (Implements30)
            {
                SettleStaticsOf(m_tree, m_options.BaseUri, loading, null);
            }

            LoadModule(m_tree, m_options.BaseUri, loading);

            // After every module, because a decimal format is the union of every declaration of it and a
            // disagreement between two is settled by a third of higher precedence.
            BuildDecimalFormats();
            BuildModes();
            SettleGlobalContextItem();

            // After every module, because an xsl:expose names components and a component may be declared in
            // any of them; before any body, because what it settles is what the package offers rather than
            // anything a body does.
            DeclareImplicitModes();
            ApplyExposures();

            // After the exposures, because what an override may replace is what the used package offers.
            CheckOverrides();

            // And after that, because what an xsl:accept may ask for is bounded by what the exposures of the
            // package it names settled on.
            CheckAcceptances();
            CheckNothingAbstract();

            // Last, because whether two components of one name are both in view depends on everything the
            // acceptances said.
            CheckHomonyms();

            // Before any body is compiled, so that an xsl:result-document naming a map finds it expanded.
            ExpandCharacterMaps();

            // Before the keys, and before any body: a template's match pattern is read here, where every
            // declaration it could name has been seen.
            CompileTemplateMatches();

            CompileKeys();
            CompileAccumulators();
            CompileAttributeSets();

            // Functions before template bodies, so that a template calling one finds it already compiled;
            // both were declared in the first pass, so the order only decides which is built first.
            CompileFunctions();
            CompileTemplateBodies();
            CheckRequiredParameters();

            // After every body, because a use-attribute-sets inside a template is only read when that
            // template is compiled — and the set it names may be declared anywhere at all.
            CheckAttributeSetsExist();
            CheckDeclaredModes();
            CheckStrictlyTypedModePatterns();
            ExpandEveryModeTemplates();

            // Last, so that a mode named here is the one the rules were recorded under rather than a fresh
            // index that nothing has a rule in. The principal module's outermost element is the one that
            // settles it: an imported module's default is its own business.
            m_tree = principal;
            m_scopeElement = FindStylesheetElement();
            int initialMode = DefaultModeIn(m_scopeElement);
            HashSet<int> eligibleModes = EligibleInitialModes(principal);

            // What an initial match selection is compiled against: the principal module's namespaces and
            // version, its functions and its decimal formats, none of which the runtime could otherwise reach.
            Dictionary<string, string> principalNamespaces = NamespacesOn(m_scopeElement);
            principalNamespaces.Remove(string.Empty);

            return new CompiledStylesheet(
                m_names,
                m_rules,
                m_globals,
                m_globalSlotCount,
                m_outputMethod,
                m_keys,
                m_outputSettings,
                WhitespaceOf(0),
                m_attributeSets,
                m_namedTemplates,
                m_modes)
            {
                BaseUri = m_stylesheetBaseUri,
                GlobalContextItem = m_globalContextItem,
                Accumulators = m_accumulators,
                ModeRules = m_modeRules,
                ModeAccumulators = m_modeAccumulators,
                InitialMode = initialMode,
                EligibleInitialModes = eligibleModes,
                Functions = m_functions,
                PackageWhitespace = m_whitespaceByPackage,
                StripInputTypeAnnotations = m_stripInputTypeAnnotations == true,
                DecimalFormats = DecimalFormatsOf(0),
                PrincipalNamespaces = principalNamespaces,
                Version = VersionOf(m_scopeElement),
                Schemas = m_schemas,
            };
        }

        /// <summary>
        /// Compiles each key's pattern and value expression, after every key name is known so that one key's
        /// <c>use</c> may itself call <c>key()</c>.
        /// </summary>
        private void CompileKeys()
        {
            foreach (KeyDefinition key in m_keys)
            {
                bool? composite = null;

                foreach (KeyRule rule in key.Rules)
                {
                    int element = rule.Element;

                    m_tree = rule.Tree;
                    m_scopeElement = element;
                    m_frameSlotCount = 0;

                    string match = GetAttribute(element, "match")
                        ?? throw new XsltException("An xsl:key must have a match pattern.");

                    rule.Patterns = Pattern.Parse(match, this);
                    rule.BackwardsCompatible = Version.IsBackwardsCompatible;

                    bool declared = ReadDeclarationFlag(element, "composite");

                    if (composite is bool earlier && earlier != declared)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1222,
                            $"Two xsl:key declarations named '{GetAttribute(element, "name")}' disagree about "
                            + "composite, and one key is composite or it is not.");
                    }

                    composite = declared;
                    key.Composite = declared;

                    string? use = GetAttribute(element, "use");
                    bool hasBody = FirstIncludedChild(element) >= 0;

                    // From XSLT 2.0 the value may come from a sequence constructor instead, which is what a
                    // key needs to sort, choose or declare a variable on the way to its value. One or the
                    // other: both is the declaration saying two different things, and neither is it saying
                    // none.
                    if (use is not null)
                    {
                        if (hasBody)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE1205,
                                "An xsl:key has both a 'use' attribute and content. The value comes from one "
                                + "or the other, and nothing decides between two of them.");
                        }

                        rule.Use = ParseExpression(element, use);
                        continue;
                    }

                    if (!hasBody)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1205,
                            "An xsl:key must say what value it files each node under, in a 'use' attribute or "
                            + "as its content.");
                    }

                    rule.Body = CompileSequence(element);
                    rule.FrameSize = m_frameSlotCount;
                }
            }
        }


        /// <summary>
        /// Builds a function item for each of the functions XSLT adds, at each arity it takes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For <c>fn:function-lookup</c>, which is handed a name while the transformation runs and has to
        /// find the function then. Every other library can be asked at that point, because every one of them
        /// is given values; these cannot, because <c>system-property</c> and its like are compiled against
        /// the namespaces in scope where they were written. So they are built here instead, where that scope
        /// still exists, and each item carries the scope of the <c>function-lookup</c> that asked — which is
        /// the static context the specification says such a lookup uses.
        /// </para>
        /// <para>
        /// Eleven names and none above three arguments, so a couple of dozen small expression trees, built
        /// only where a stylesheet writes <c>function-lookup</c> at all. A name that will not build at some
        /// arity is simply left out, which is the same answer the lookup would have given.
        /// </para>
        /// </remarks>
        private Dictionary<(ExpandedName Name, int Arity), XPathValue> BuildHostFunctionItems()
        {
            Dictionary<(ExpandedName, int), XPathValue> items = new();

            foreach ((string local, (int Least, int Most) arities) in Availability.XsltFunctions)
            {
                for (int arity = arities.Least; arity <= arities.Most; arity++)
                {
                    Expr? body;

                    try
                    {
                        body = TryCreateFunction(local, FunctionLookupExpr.Placeholders(arity));
                    }
                    catch (XsltException)
                    {
                        continue;
                    }

                    if (body is null)
                    {
                        continue;
                    }

                    items[(new ExpandedName(XdmType.FunctionNamespace, local), arity)] =
                        XPathValue.FromFunction(new XdmNamedFunction(
                            new XdmQName("fn", XdmType.FunctionNamespace, local), arity, body));
                }
            }

            return items;
        }

        /// <inheritdoc/>
        public Expr? TryCreateFunction(string name, Expr[] arguments)
        {
            // A static expression is answered while the stylesheet is being prepared, so a function that
            // reads the source document or the state of a running transformation has nothing to read
            // (§3.11.2). It is refused where it is written rather than answered with something invented.
            if (m_inUseWhen && Availability.IsBarredFromStaticExpressions(name, Implements30))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'{name}()' is not available in a static expression: a use-when or a shadow attribute "
                    + "is answered before there is a source document to read, a transformation running, or "
                    + "any output to name.");
            }

            if (name == "format-number")
            {
                if (arguments.Length is < 2 or > 3)
                {
                    throw new XsltException(
                        $"Function 'format-number()' expects 2 or 3 arguments, but {arguments.Length} were supplied.");
                }

                if (arguments.Length == 3 && arguments[2] is not StringLiteralExpr)
                {
                    // XSLT 1.0 requires a literal here. From 3.0 the name may be computed, and the core
                    // library's own form of the call handles that against this same static context.
                    return Version.IsBackwardsCompatible
                        ? throw new XsltException(
                            "The decimal format name passed to 'format-number()' must be a literal.")
                        : new NamedDecimalFormatExpr(
                            arguments[0],
                            arguments[1],
                            arguments[2],
                            new DecimalFormatScope(
                                m_names, Version, SyntaxVersion, NamespacesOn(m_scopeElement), DecimalFormatsOf(CurrentPackage)),
                            Version);
                }

                string formatName = arguments.Length == 3
                    ? ((StringLiteralExpr)arguments[2]).Value
                    : string.Empty;

                return new FormatNumberExpr(
                    arguments[0],
                    arguments[1],
                    NamedDecimalFormatExpr.Resolve(formatName, this),
                    Version,
                    SyntaxVersion);
            }

            if (name == "stream-available" && Implements30)
            {
                if (arguments.Length != 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"'stream-available()' takes one argument, a URI, and {arguments.Length} were "
                        + "supplied.");
                }

                // The base URI is this element's, which is what a relative reference resolves against — the
                // same base doc() reads one against.
                return new StreamAvailableExpr(arguments[0], StaticBaseUri(m_scopeElement));
            }

            if (name == "transform" && Implements30)
            {
                if (arguments.Length != 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"'transform()' takes one argument, a map of options, and {arguments.Length} were "
                        + "supplied.");
                }

                // Built here rather than in the function library because running a transformation means
                // reaching the resolvers the caller configured, and the library knows of no caller. The base
                // URI is this element's, which is what a stylesheet-location resolves against.
                return new TransformExpr(arguments[0], m_options, StaticBaseUri(m_scopeElement));
            }

            if (name == "system-property")
            {
                if (arguments.Length != 1)
                {
                    throw new XsltException(
                        $"Function 'system-property()' expects 1 argument, but {arguments.Length} were supplied.");
                }

                // The usual case is a literal, and a system property cannot change while a transformation
                // runs, so it folds to a constant here rather than being looked up on every call. A literal
                // that is not a name is left to the run-time form, which knows the code for it: the
                // specification makes that a dynamic error, and folding it would answer "no such property"
                // to a question that was never a question.
                if (arguments[0] is StringLiteralExpr propertyLiteral
                    && IsResolvableName(propertyLiteral.Value))
                {
                    ExpandedName property = ResolveQualifiedName(m_scopeElement, propertyLiteral.Value);
                    XPathValue value = SystemProperty.Lookup(
                        property.NamespaceUri,
                        property.LocalName,
                        Version,
                        SyntaxVersion,
                        m_options.DynamicEvaluation,
                        m_options.SchemaAware);

                    return value.Kind == XPathValueKind.Number
                        ? new NumberLiteralExpr(value.ToNumber())
                        : new StringLiteralExpr(value.ToStringValue());
                }

                return new SystemPropertyExpr(
                    arguments[0],
                    PrefixesInScope(),
                    Version,
                    SyntaxVersion,
                    m_options.DynamicEvaluation,
                    m_options.SchemaAware);
            }

            if (name == "available-system-properties" && Implements30)
            {
                if (arguments.Length != 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"Function 'available-system-properties()' takes no argument, and {arguments.Length} "
                        + "were supplied.");
                }

                // Every property the processor answers, as QNames. A constant, for the reason
                // system-property() folds: nothing about the processor changes while a transformation runs.
                return new TypedLiteralExpr(SystemProperty.AvailableNames());
            }

            if (name == "static-base-uri" && arguments.Length == 0)
            {
                // Static, as the name says: the base URI where the call is written, known now. Folding it
                // here is also what makes it answerable from a use-when, which is answered before any
                // transformation runs and so has no runtime to ask.
                return StaticBaseUri(m_scopeElement) is string baseUri
                    ? new TypedLiteralExpr(XPathValue.FromAnyUri(baseUri))
                    : new EmptySequenceExpr();
            }

            if (name == "resolve-uri" && arguments.Length == 1
                && StaticBaseUri(m_scopeElement) is string against)
            {
                // The base a one-argument call resolves against is the static one — where the expression was
                // written, which xml:base may have moved — rather than wherever the transformation started.
                return Xpath2FunctionExpr.TryCreate(
                    "resolve-uri", new[] { arguments[0], new StringLiteralExpr(against) }, Version);
            }

            if (name is "doc" or "doc-available"
                && Xpath2FunctionExpr.TryCreate(name, arguments, Version, SyntaxVersion)
                    is Xpath2FunctionExpr reading)
            {
                // A relative reference resolves against the base URI where the call is written, which an
                // xml:base may have moved — the same base document() reads one against. The library form
                // has no stylesheet to ask, so the base is handed to it here.
                reading.StaticBaseUri = StaticBaseUri(m_scopeElement);
                reading.Package = CurrentPackage;
                return reading;
            }

            if (name is "collection" or "uri-collection")
            {
                // The same base and the same package as doc(), for the same reason: a relative collection
                // URI resolves against where the call is written, and what the collection's documents are
                // stripped of is the calling package's business. uri-collection() is 3.0's, so it is built
                // only where the library would build it.
                bool thirty = Version.CompareTo(XsltVersion.V30) >= 0
                    || SyntaxVersion.CompareTo(XsltVersion.V30) >= 0;

                Expr? asking = name == "collection"
                    ? Xpath2FunctionExpr.TryCreate(name, arguments, Version, SyntaxVersion)
                    : thirty ? Xpath30FunctionExpr.TryCreate(name, arguments, Version) : null;

                if (asking is CollectionFunctionExpr call)
                {
                    call.StaticBaseUri = StaticBaseUri(m_scopeElement);
                    call.Package = CurrentPackage;
                    return call;
                }
            }

            if (name is "environment-variable" or "available-environment-variables"
                && (Version.CompareTo(XsltVersion.V30) >= 0 || SyntaxVersion.CompareTo(XsltVersion.V30) >= 0)
                && Xpath30FunctionExpr.TryCreate(name, arguments, Version) is EnvironmentVariableExpr environment)
            {
                // Whether the environment is visible is the caller's decision, made in the options. A
                // running transformation reads it from its own options; a static expression, a use-when,
                // runs before any transformation does, so the decision is handed over here.
                environment.EnabledStatically = m_options.EnvironmentVariablesEnabled;
                return environment;
            }

            if (name == "copy-of" && arguments.Length <= 1 && Implements30)
            {
                return new CopyOfFunctionExpr(arguments.Length == 0 ? null : arguments[0]);
            }

            if (name == "snapshot" && arguments.Length <= 1 && Implements30)
            {
                return new SnapshotFunctionExpr(arguments.Length == 0 ? null : arguments[0]);
            }

            if (name is "accumulator-before" or "accumulator-after" && Implements30)
            {
                if (arguments.Length != 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"'{name}()' takes one argument, the name of an accumulator.");
                }

                if (arguments[0] is not StringLiteralExpr accumulatorName)
                {
                    // A computed name is checked when it is known, against the same declarations and with the
                    // prefixes in scope here.
                    return new AccumulatorExpr(
                        arguments[0],
                        name == "accumulator-after",
                        NamespacesOn(m_scopeElement),
                        m_accumulators,
                        CurrentPackage);
                }

                ExpandedName accumulator = ResolveQualifiedName(m_scopeElement, accumulatorName.Value);

                if (!m_accumulatorsByName.TryGetValue(Scoped(accumulator), out AccumulatorDefinition? declared))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE3340,
                        $"No xsl:accumulator is named '{accumulatorName.Value}'.");
                }

                return new AccumulatorExpr(declared.Index, name == "accumulator-after", accumulatorName.Value);
            }

            if (name is "current-group" or "current-grouping-key" or "regex-group"
                or "current-merge-group" or "current-merge-key" or "current-output-uri")
            {
                // current-merge-group takes an optional source name; the rest are fixed.
                if (name == "current-merge-group" && arguments.Length <= 1)
                {
                    return new ContextualFunctionExpr(
                        name, arguments.Length == 0 ? null : arguments[0], Implements30);
                }

                int wanted = name == "regex-group" ? 1 : 0;
                if (arguments.Length != wanted)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"Function '{name}()' expects {wanted} argument(s), but {arguments.Length} were supplied.");
                }

                return new ContextualFunctionExpr(
                    name, arguments.Length == 0 ? null : arguments[0], Implements30);
            }

            if (name == Availability.FunctionLookup)
            {
                if (arguments.Length != 2)
                {
                    throw new XsltException(
                        "Function 'function-lookup()' expects 2 arguments, but "
                        + $"{arguments.Length} were supplied.");
                }

                return new FunctionLookupExpr(
                    arguments[0],
                    arguments[1],
                    FunctionsSeenIn(CurrentPackage),
                    BuildHostFunctionItems(),
                    Version,
                    SyntaxVersion,
                    LegacySyntax);
            }

            if (name == "function-available")
            {
                // XSLT 2.0 gave it a second argument, the arity: a name alone asks whether there is any such
                // function, and a name with an arity asks whether one can be called that way.
                if (arguments.Length is < 1 or > 2)
                {
                    throw new XsltException(
                        "Function 'function-available()' expects 1 or 2 arguments, but "
                        + $"{arguments.Length} were supplied.");
                }

                // Not settled here even for a literal name, unlike the two below. A stylesheet function
                // counts as available, and whether one of a given name is declared is not known until every
                // module has been read — which is after this expression is built.
                return new FunctionAvailableExpr(
                    arguments[0],
                    PrefixesInScope(),
                    m_functions,
                    Version,
                    SyntaxVersion,
                    arguments.Length == 2 ? arguments[1] : null,
                    m_inUseWhen);
            }

            if (name is "element-available" or "type-available")
            {
                if (arguments.Length != 1)
                {
                    throw new XsltException(
                        $"Function '{name}()' expects 1 argument, but {arguments.Length} were supplied.");
                }

                // What this engine implements cannot change while it runs, so a literal name — which is what
                // the guarded-call pattern always writes — settles here and costs nothing afterwards. A
                // literal that is not a name is left to the run-time form, which knows the code for it: the
                // specification makes that a dynamic error, and a stylesheet with such a call on a branch
                // nothing takes is entitled to compile.
                if (arguments[0] is StringLiteralExpr nameLiteral && IsResolvableName(nameLiteral.Value))
                {
                    ExpandedName probed = ResolveQualifiedName(m_scopeElement, nameLiteral.Value);

                    return new BooleanLiteralExpr(name == "element-available"
                        ? Availability.IsElementAvailable(
                            probed.NamespaceUri, probed.LocalName, Implements30, m_options.DynamicEvaluation)
                        : Availability.IsTypeAvailable(probed.NamespaceUri, probed.LocalName, Implements30)
                            || m_schemas?.IsTypeAvailable(probed.NamespaceUri, probed.LocalName) == true);
                }

                return name == "element-available"
                    ? new ElementAvailableExpr(
                        arguments[0], PrefixesInScope(), Implements30, m_options.DynamicEvaluation)
                    : new TypeAvailableExpr(arguments[0], PrefixesInScope(), Implements30, m_schemas);
            }

            if (name is "unparsed-entity-uri" or "unparsed-entity-public-id")
            {
                // XSLT 3.0 added the second argument, the node whose document is asked about.
                int most = Implements30 ? 2 : 1;

                if (arguments.Length < 1 || arguments.Length > most)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"Function '{name}()' expects 1{(most == 2 ? " or 2" : string.Empty)} argument(s), but "
                        + $"{arguments.Length} were supplied.");
                }

                return new UnparsedEntityExpr(
                    arguments[0], arguments.Length == 2 ? arguments[1] : null, name == "unparsed-entity-uri");
            }

            if (name == "current")
            {
                if (arguments.Length != 0)
                {
                    throw new XsltException(
                        $"Function 'current()' expects no arguments, but {arguments.Length} were supplied.");
                }

                return new CurrentExpr(Version);
            }

            if (name == "document")
            {
                if (arguments.Length is < 1 or > 2)
                {
                    throw new XsltException(
                        $"Function 'document()' expects 1 or 2 arguments, but {arguments.Length} were supplied.");
                }

                return new DocumentExpr(
                    arguments[0],
                    StaticBaseUri(m_scopeElement),
                    arguments.Length == 2 ? arguments[1] : null,
                    CurrentPackage);
            }

            // XSLT's own until 3.0, which moved it into the core function library and narrowed the argument
            // from a node-set to one node or none. A 3.0 stylesheet is handed to the library form by
            // declining it here, so that one stylesheet does not get 1.0's reading of a name 3.0 defines.
            if (name == "generate-id" && Version < XsltVersion.V30)
            {
                if (arguments.Length > 1)
                {
                    throw new XsltException(
                        $"Function 'generate-id()' expects 0 or 1 arguments, but {arguments.Length} were supplied.");
                }

                return new GenerateIdExpr(arguments.Length == 0 ? null : arguments[0]);
            }

            // id, idref and element-with-id are not built here. They are XPath's own functions and need
            // nothing that was in scope where they were written, so they live in FunctionLibrary with the
            // rest of the core library -- which is also what lets an expression outside a stylesheet call
            // one. Returning nothing here is what sends them there.

            // A prefixed name, or one written Q{uri}local, names a stylesheet function or an extension.
            if (name.IndexOf(':') >= 0 || (name.StartsWith("Q{", StringComparison.Ordinal) && Implements30))
            {
                ExpandedName extension = ResolveQualifiedName(m_scopeElement, name);

                if (m_inUseWhen)
                {
                    // Neither what the stylesheet declares nor what an extension might supply is in reach
                    // here, and there is no "only if it is reached" to fall back on: a use-when is being
                    // evaluated now, so a function it cannot call is a mistake now.
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"'{name}()' cannot be called from a use-when, which is answered before the "
                        + "stylesheet declaring it has been read.");
                }

                // A function the stylesheet declared with xsl:function. Name and arity together identify it,
                // so the same name with a different number of arguments is a different function — or, if
                // none was declared with this many, no function at all.
                UserFunction? declared = extension.Equals(XsltOriginal)
                    ? (m_compilingFunction is { } compiling && compiling.Arity == arguments.Length
                        ? compiling.Original
                        : null)
                    : m_functions.TryGetValue((extension, arguments.Length), out UserFunction? named) ? named : null;

                if (declared is not null)
                {
                    // xsl:original is not a name in any package's space: it means the function this one
                    // replaced, which is by definition one the override was entitled to reach — and it is
                    // resolved against the function being compiled, so that each override means its own.
                    if (!extension.Equals(XsltOriginal))
                    {
                        CheckReference(
                            "function",
                            extension,
                            arguments.Length,
                            FunctionPackage(declared),
                            XsltErrorCode.XPST0017,
                            $"{name}#{arguments.Length}");
                    }

                    return new UserFunctionCallExpr(declared, arguments);
                }

                if (m_functions.Keys.Any(key => key.Name.Equals(extension)))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"No xsl:function named '{name}' takes {arguments.Length} argument(s).");
                }

                // Otherwise an extension function, and this engine provides none. Under XSLT 1.0 behaviour
                // the call is compiled all the same so that it only fails if it is reached — see
                // UnavailableFunctionExpr — which is what lets a function-available() guard mean anything.
                // From 2.0 the specification makes the unknown function a static error, and gives a
                // stylesheet use-when to guard with instead.
                if (Version.IsBackwardsCompatible)
                {
                    return new UnavailableFunctionExpr(name, extension.NamespaceUri, arguments);
                }

                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'{name}()' is not a function this stylesheet declares or this engine provides. From "
                    + "XSLT 2.0 a call to an unknown function is refused when the stylesheet is read; guard "
                    + "it with use-when, or under version=\"1.0\" with function-available().");
            }

            if (name != "key")
            {
                return null;
            }

            // XSLT 2.0 adds a third argument naming which document to search.
            if (arguments.Length is not (2 or 3))
            {
                throw new XsltException(
                    $"Function 'key()' expects 2 or 3 arguments, but {arguments.Length} were supplied.");
            }

            Expr? within = arguments.Length == 3 ? arguments[2] : null;

            // A literal name — overwhelmingly the usual case — resolves now, so the lookup costs nothing at
            // run time.
            if (arguments[0] is StringLiteralExpr literal)
            {
                return new KeyExpr(FindKeyIndex(literal.Value), null, arguments[1], within)
                {
                    BackwardsCompatible = Version.IsBackwardsCompatible,
                };
            }

            // A name computed while the transformation runs is resolved in the namespaces in scope where
            // the call is written (§20.2.2), which have to travel with it.
            return new KeyExpr(-1, arguments[0], arguments[1], within)
            {
                Package = CurrentPackage,
                Namespaces = NamespacesOn(m_scopeElement),
                BackwardsCompatible = Version.IsBackwardsCompatible,
            };
        }

        /// <summary>
        /// Captures the namespace bindings in scope where the expression being compiled was written, for a
        /// function whose argument names something only at run time.
        /// </summary>
        /// <summary>Whether text is a name whose prefix, if it has one, is bound where the call was written.</summary>
        private bool IsResolvableName(string text)
        {
            if (!IsLexicalQName(text))
            {
                return false;
            }

            int colon = text.IndexOf(':');
            return colon < 0 || m_tree.ResolvePrefix(m_scopeElement, text[..colon]) is not null;
        }

        private Dictionary<string, string> PrefixesInScope()
        {
            return NamespacesOn(m_scopeElement);
        }

        /// <summary>
        /// Collects the namespace declarations in scope on one stylesheet element.
        /// </summary>
        /// <remarks>
        /// Taken at compilation and carried into the instruction, because a name computed at run time is
        /// still read against the declarations where it was <em>written</em> — the input document being
        /// transformed has its own namespaces and none of them are in scope here.
        /// </remarks>
        /// <param name="element">The stylesheet element.</param>
        /// <returns>Each prefix in scope and what it stands for, the default namespace under an empty key.</returns>
        private Dictionary<string, string> NamespacesOn(int element)
        {
            Dictionary<string, string> prefixes = new(StringComparer.Ordinal);

            foreach ((string prefix, string uri) in m_tree.InScopeNamespacesOf(element))
            {
                prefixes[prefix] = uri;
            }

            return prefixes;
        }

        /// <inheritdoc/>
        public DecimalFormat? ResolveDecimalFormat(ExpandedName name)
        {
            if (m_decimalFormats.TryGetValue(Scoped(name), out DecimalFormat? format))
            {
                return format;
            }

            // A stylesheet that declares no unnamed format still has one, being the symbols the
            // specification names as the default.
            return name.LocalName.Length == 0 ? DecimalFormat.Default : null;
        }

        /// <summary>
        /// Reads an <c>xsl:decimal-format</c>, which renames the characters that patterns are written with and
        /// that results are written in.
        /// </summary>
        private void ReadDecimalFormat(int element, int precedence)
        {
            // Keyed by expanded name, so two prefixes bound to one namespace declare the same format and one
            // prefix rebound declares a different one.
            ExpandedName name = Scoped(GetAttribute(element, "name") is string qualified
                ? ResolveQualifiedName(element, qualified)
                : new ExpandedName(string.Empty, string.Empty));

            int count = m_tree.AttributeCountOf(element);
            NameTable names = m_tree.NameTable;

            for (int i = 0; i < count; i++)
            {
                int attribute = m_tree.AttributeAt(element, i);
                int fingerprint = m_tree.FingerprintOf(attribute);

                if (names.GetNamespaceUri(fingerprint).Length != 0)
                {
                    continue;
                }

                string property = names.GetLocalName(fingerprint);

                if (property == "name" || Array.IndexOf(XsltElements.Standard, property) >= 0)
                {
                    continue;
                }

                string value = m_tree.StringValueOf(attribute);

                if (!m_decimalFormatProperties.TryGetValue((name, property), out DecimalFormatProperty stated))
                {
                    m_decimalFormatProperties[(name, property)] =
                        new DecimalFormatProperty(precedence, value, false);
                    continue;
                }

                if (stated.Precedence > precedence)
                {
                    // A module that imports this one has already spoken, and outranks it.
                    continue;
                }

                m_decimalFormatProperties[(name, property)] = stated.Precedence < precedence
                    ? new DecimalFormatProperty(precedence, value, false)
                    : new DecimalFormatProperty(precedence, value, stated.Conflicts || stated.Value != value);
            }

            // Declared even where every property came from somewhere else, so that a format named only by an
            // empty declaration is still a format the stylesheet has.
            m_decimalFormats.TryAdd(name, DecimalFormat.Default);
        }

        /// <summary>
        /// One property of one named decimal format: what it was last given, at what import precedence, and
        /// whether two declarations of that precedence disagreed about it.
        /// </summary>
        /// <remarks>
        /// A disagreement is recorded rather than thrown, because a declaration of higher precedence read
        /// later settles it — that is what import precedence is for. Only what survives to the end is an
        /// error.
        /// </remarks>
        private readonly record struct DecimalFormatProperty(int Precedence, string Value, bool Conflicts);

        /// <summary>
        /// Builds each named decimal format from every declaration of it, and refuses what is still ambiguous.
        /// </summary>
        /// <remarks>
        /// Deferred to the end of the declaration pass for two reasons. A format is the union of its
        /// declarations, so no one of them can be read on its own; and two declarations of one precedence
        /// giving one property two values is an error only if nothing of higher precedence overrides it,
        /// which is not known until every module has been read.
        /// </remarks>
        private void BuildDecimalFormats()
        {
            foreach (ExpandedName name in m_decimalFormats.Keys.ToArray())
            {
                DecimalFormat format = new DecimalFormat();

                format.DecimalSeparator = Character(name, "decimal-separator") ?? format.DecimalSeparator;
                format.GroupingSeparator = Character(name, "grouping-separator") ?? format.GroupingSeparator;
                format.ExponentSeparator = Character(name, "exponent-separator") ?? format.ExponentSeparator;
                format.MinusSign = Character(name, "minus-sign") ?? format.MinusSign;
                format.Percent = Character(name, "percent") ?? format.Percent;
                format.PerMille = Character(name, "per-mille") ?? format.PerMille;
                format.Digit = Character(name, "digit") ?? format.Digit;
                format.PatternSeparator = Character(name, "pattern-separator") ?? format.PatternSeparator;

                if (Stated(name, "infinity") is string infinity)
                {
                    format.Infinity = infinity;
                }

                if (Stated(name, "NaN") is string notANumber)
                {
                    format.NaN = notANumber;
                }

                if (Character(name, "zero-digit") is int zero)
                {
                    format.ZeroDigit = zero;

                    // It names the start of a run of ten digits, so it has to be a digit and it has to be the
                    // first of its ten. '2' is a digit and is not, which is why this is its own error.
                    string zeroDigit = Stated(name, "zero-digit")!;

                    if (System.Globalization.CharUnicodeInfo.GetDecimalDigitValue(zeroDigit, 0) != 0)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1295,
                            $"'{zeroDigit}' cannot be a zero-digit. It names the first of the ten digits a "
                            + "picture counts in, so it has to be a digit whose value is zero.");
                    }
                }

                RequireDistinctRoles(format);
                m_decimalFormats[name] = format;
            }

            string? Stated(ExpandedName name, string property)
            {
                if (!m_decimalFormatProperties.TryGetValue((name, property), out DecimalFormatProperty p))
                {
                    return null;
                }

                if (p.Conflicts)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE1290,
                        $"Two xsl:decimal-format declarations of the same import precedence disagree about "
                        + $"'{property}', and nothing of higher precedence settles it.");
                }

                return p.Value;
            }

            int? Character(ExpandedName name, string property)
            {
                return Stated(name, property) is { Length: > 0 } value
                    ? RequireOneCharacter(property, value)
                    : null;
            }
        }

        /// <summary>
        /// Reads an <c>xsl:decimal-format</c> attribute that names one character, refusing anything longer.
        /// </summary>
        /// <remarks>
        /// One character, not one <see cref="char"/>: a character outside the basic plane is written as a
        /// surrogate pair and is still one character, which is why this counts by code point. Taking the
        /// first character of a longer value and discarding the rest would let
        /// <c>decimal-separator="comma"</c> quietly mean <c>c</c>.
        /// </remarks>
        private static int RequireOneCharacter(string attribute, string value)
        {
            int code = char.ConvertToUtf32(value, 0);

            if (value.Length == (code > 0xFFFF ? 2 : 1))
            {
                return code;
            }

            throw XsltErrors.Error(
                XsltErrorCode.XTSE0020,
                $"'{value}' cannot be the '{attribute}' of an xsl:decimal-format. It stands for one "
                + "character in a picture, so it has to be one character.");
        }

        /// <summary>
        /// Refuses a format that gives one character two jobs.
        /// </summary>
        /// <remarks>
        /// A picture is read character by character, and every one of these means something different there.
        /// Two roles sharing a character makes the reading ambiguous at best and silently wrong at worst —
        /// <c>zero-digit="0" digit="0"</c> gives no way to tell a digit that must appear from one that may.
        /// <para>
        /// The digits following <c>zero-digit</c> count too: it names the start of a run of ten, and any of
        /// those ten standing for something else has the same effect.
        /// </para>
        /// </remarks>
        private static void RequireDistinctRoles(DecimalFormat format)
        {
            (int Code, string Name)[] roles =
            {
                (format.DecimalSeparator, "decimal-separator"),
                (format.GroupingSeparator, "grouping-separator"),
                (format.ExponentSeparator, "exponent-separator"),
                (format.Percent, "percent"),
                (format.PerMille, "per-mille"),
                (format.Digit, "digit"),
                (format.PatternSeparator, "pattern-separator"),
            };

            for (int i = 0; i < roles.Length; i++)
            {
                if (roles[i].Code - format.ZeroDigit is >= 0 and <= 9)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE1300,
                        $"The '{roles[i].Name}' of this xsl:decimal-format is one of the ten digits its "
                        + "zero-digit begins, so a picture using it would say two things at once.");
                }

                for (int j = 0; j < i; j++)
                {
                    if (roles[i].Code == roles[j].Code)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1300,
                            $"This xsl:decimal-format gives one character to both '{roles[j].Name}' and "
                            + $"'{roles[i].Name}', so a picture using it would say two things at once.");
                    }
                }
            }
        }

        /// <summary>
        /// Finds the key a name stands for.
        /// </summary>
        /// <remarks>
        /// By expanded name, prefix and all. Two keys may share a local name in different namespaces, and a
        /// prefix bound to something no key is in names no key — comparing local names alone answered
        /// <c>key('my:k', …)</c> with the <c>k</c> in no namespace, which is a different key.
        /// </remarks>
        /// <param name="name">The name as written in the call.</param>
        private int FindKeyIndex(string name)
        {
            if (IsResolvableName(name))
            {
                ExpandedName wanted = ResolveQualifiedName(m_scopeElement, name);

                foreach (KeyDefinition key in m_keys)
                {
                    // A key is local to the package that declared it (§3.5.3): a library's key of one name
                    // and the principal's of the same name are two keys, and each package sees its own.
                    if (key.Name == wanted && key.Package == CurrentPackage)
                    {
                        return key.Index;
                    }
                }
            }

            throw XsltErrors.Error(
                XsltErrorCode.XTDE1260, $"No xsl:key named '{name}' is declared.");
        }

        /// <summary>
        /// Locates a module's document element, and reports whether it is a simplified stylesheet.
        /// </summary>
        /// <remarks>
        /// A simplified stylesheet is just the result document with <c>xsl:version</c> on its root — no
        /// <c>xsl:stylesheet</c> wrapper and no declarations. It stands for a stylesheet holding one template
        /// matching <c>/</c> whose body is that element, which is how it is compiled here.
        /// </remarks>
        /// <param name="simplified">On return, whether the document element is a literal result element.</param>
        private int FindStylesheetElement(out bool simplified) => FindStylesheetElement(-1, out simplified);

        /// <summary>
        /// Locates a module outermost element, starting at a given element rather than at the document.
        /// </summary>
        /// <param name="from">
        /// The element the module starts at, for a reference that named one by a fragment identifier, or
        /// -1 to take the document element as every ordinary reference does.
        /// </param>
        /// <param name="simplified">On return, whether that element is a literal result element.</param>
        private int FindStylesheetElement(int from, out bool simplified)
        {
            simplified = false;

            if (from >= 0)
            {
                // An embedded stylesheet: the module is an element inside a document that is something
                // else, and what is around it is none of the stylesheet business. It is held to the same
                // rules as a document element would be, since it is one as far as the module goes.
                if (!IsXsltElement(from, out string embedded))
                {
                    if (GetXsltAttribute(from, "version") is not null)
                    {
                        simplified = true;
                        return from;
                    }

                    throw new XsltException(
                        $"The element a reference named by its identifier is '{LocalNameOf(from)}', but a "
                        + "stylesheet module must start with xsl:stylesheet or xsl:transform, or be a "
                        + "literal result element carrying an xsl:version attribute.");
                }

                if (embedded is not ("stylesheet" or "transform" or "package"))
                {
                    throw new XsltException(
                        $"The element a reference named by its identifier is 'xsl:{embedded}', which is "
                        + "not a stylesheet module.");
                }

                return from;
            }

            // Deliberately not skipping what use-when excludes. On the stylesheet element itself the
            // attribute cannot remove the element — there would then be no stylesheet to have written it —
            // so what it removes is everything inside, which GatherTopLevel sees to.
            for (int child = m_tree.FirstChildOf(XdmTree.RootNode); child >= 0; child = m_tree.NextSiblingOf(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element)
                {
                    continue;
                }

                if (!IsXsltElement(child, out string localName))
                {
                    if (GetXsltAttribute(child, "version") is not null)
                    {
                        simplified = true;
                        return child;
                    }

                    throw new XsltException(
                        $"The document element is '{LocalNameOf(child)}', but a stylesheet must start with "
                        + "xsl:stylesheet or xsl:transform, or be a literal result element carrying "
                        + "an xsl:version attribute.");
                }

                // xsl:package is the third way a stylesheet module can start, and everything below this
                // point treats it as a fourth spelling of xsl:stylesheet. What makes it a package rather
                // than a stylesheet is its name, its version, and the visibility its components declare —
                // none of which changes how the declarations inside it are read.
                if (localName is "stylesheet" or "transform" or "package")
                {
                    if (GetAttribute(child, "version") is null)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0010,
                            $"'{QualifiedNameOf(child)}' requires a 'version' attribute. It is what says "
                            + "which language the stylesheet is written in.");
                    }

                    return child;
                }

                throw new XsltException($"Unexpected top-level element 'xsl:{localName}'.");
            }

            throw new XsltException("The stylesheet is empty.");
        }

        private int FindStylesheetElement() => FindStylesheetElement(out _);

        /// <summary>
        /// Reads one stylesheet module and everything it references.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The order here is what produces correct import precedence. <c>xsl:include</c> is textual, so an
        /// included module's declarations are gathered into the including module's list and share its
        /// precedence. <c>xsl:import</c> is not: each imported module is loaded first and given a lower
        /// precedence number, so that by the time this module's own declarations are recorded, everything it
        /// imported already ranks below it.
        /// </para>
        /// <para>
        /// Because imports are processed depth-first before the importing module, precedence numbers come out
        /// increasing towards the stylesheet the caller supplied, which is exactly the ordering template
        /// conflict resolution needs.
        /// </para>
        /// </remarks>
        /// <param name="tree">The module to read.</param>
        /// <param name="uri">The module's identity, or <see langword="null"/> for the supplied stylesheet.</param>
        /// <param name="loading">The modules currently being read, used to catch a reference cycle.</param>
        private void LoadModule(XdmTree tree, string? uri, HashSet<string> loading, int from = -1)
        {
            List<(ModuleElement Source, string? Uri)> topLevel = new();
            GatherTopLevel(tree, uri, topLevel, loading, from);

            // The first precedence the imports of this module will be given. Precedences are handed out
            // depth-first, and imports are followed before the importing module takes its own, so what a
            // module imported -- directly or through those imports -- is exactly the precedences from
            // here up to its own. That range is what an xsl:apply-imports inside it may reach: the rules
            // it may override are the ones it imported, and not every rule that happens to be beneath it.
            int floor = m_nextPrecedence;

            // Every module of a package, the included ones too, is compiled as part of that package. A body
            // compiled later has to know whose it is, and this is where that is known.
            foreach ((ModuleElement source, string? moduleUri) in topLevel)
            {
                m_packageOfModule[source.Tree] = m_package;
                m_moduleUris[source.Tree] = moduleUri;
            }

            // Every use-when in the module is answered while the stylesheet is being prepared (§3.11), and
            // not only the ones the compiler will later walk past: an element inside an xsl:fallback that
            // nothing will ever fall back to still carries one, and a mistake in it is a mistake all the
            // same. The top-level ones are answered by the gathering above, which leaves out what they
            // remove; this reaches the rest.
            foreach ((ModuleElement source, string? _) in topLevel)
            {
                m_tree = source.Tree;
                SettleUseWhenIn(source.Element);
            }

            // Imports first, so their declarations land at a lower precedence than this module's.
            foreach ((ModuleElement source, string? moduleUri) in topLevel)
            {
                m_tree = source.Tree;
                if (!IsXsltElement(source.Element, out string localName) || localName != "import")
                {
                    continue;
                }

                // Checked before it is followed, since following it is what this element does: nothing else
                // ever looks at what an xsl:import carries.
                m_scopeElement = source.Element;
                ValidateXsltElement(source.Element, localName, XsltPlacement.Declaration);

                LoadReferencedModule(source.Element, moduleUri, loading, "import");
            }

            // Used packages come in with the imports and for the same reason: everything they declare has to
            // be recorded before this module's own declarations, so that this module's win.
            foreach ((ModuleElement source, string? _) in topLevel)
            {
                m_tree = source.Tree;
                if (IsXsltElement(source.Element, out string localName)
                    && localName == "use-package"
                    && Implements30)
                {
                    m_scopeElement = source.Element;
                    ValidateXsltElement(source.Element, localName, XsltPlacement.Declaration);

                    // Using a package is something the principal module of a package does, or a module it
                    // includes. A module brought in by xsl:import is at a precedence of its own, and a used
                    // package has none.
                    if (m_importDepth > 0)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3008,
                            "An xsl:use-package stands in an imported module. Only the principal module of a "
                            + "package, or a module it includes, may use a package.");
                    }

                    LoadUsedPackage(source.Element, loading);
                }
            }

            // Schemas before declarations, whichever comes first in the module: a declaration's 'as' may
            // name a type an import brings into scope, and the declarations are read in order.
            if (m_schemas is not null)
            {
                foreach ((ModuleElement source, string? moduleUri) in topLevel)
                {
                    m_tree = source.Tree;

                    if (IsXsltElement(source.Element, out string localName) && localName == "import-schema")
                    {
                        m_scopeElement = source.Element;
                        ValidateXsltElement(source.Element, localName, XsltPlacement.Declaration);
                        ImportSchema(source.Element, moduleUri);
                    }
                }

                // Compiled now rather than on first use, so that a schema that is not a valid one is a
                // static error of the stylesheet whether or not anything goes on to name its types.
                m_schemas.EnsureCompiled();
            }

            int precedence = m_nextPrecedence++;
            int outerFloor = m_importFloor;
            m_importFloor = floor;

            foreach ((ModuleElement source, string? _) in topLevel)
            {
                m_modulePrecedence[source.Tree] = precedence;
            }

            foreach ((ModuleElement source, string? _) in topLevel)
            {
                m_tree = source.Tree;
                if (IsXsltElement(source.Element, out string localName) && localName == "import")
                {
                    continue;
                }

                DeclareTopLevelElement(source, precedence);
            }

            m_importFloor = outerFloor;
        }

        /// <summary>
        /// Imports the schema one <c>xsl:import-schema</c> names: by location, inline, or by namespace alone.
        /// </summary>
        /// <param name="element">The <c>xsl:import-schema</c> element.</param>
        /// <param name="moduleUri">The module's URI, which a relative location resolves against.</param>
        private void ImportSchema(int element, string? moduleUri)
        {
            string? targetNamespace = GetAttribute(element, "namespace");
            string? location = GetAttribute(element, "schema-location");
            string? inline = null;

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element)
                {
                    continue;
                }

                int fingerprint = m_tree.FingerprintOf(child);

                if (m_tree.NameTable.GetNamespaceUri(fingerprint) == XdmType.SchemaNamespace
                    && m_tree.NameTable.GetLocalName(fingerprint) == "schema")
                {
                    // Handed to the schema reader as text, which is the one form it reads; serialized
                    // with its in-scope namespaces, so that a prefix declared on the stylesheet element is
                    // there for the schema to use.
                    inline = Serializer.Serialize(
                        XdmSequence.Items(XPathValue.FromNodeSet(NodeSet.Singleton(m_tree, child))),
                        XPathValue.FromSequence(XdmSequence.Empty));
                    break;
                }
            }

            m_schemas!.Import(targetNamespace, location, inline, StaticBaseUri(element) ?? moduleUri);
        }

        /// <summary>
        /// Collects a module's top-level declarations, splicing in those of anything it includes.
        /// </summary>
        private void GatherTopLevel(
            XdmTree tree,
            string? uri,
            List<(ModuleElement Source, string? Uri)> topLevel,
            HashSet<string> loading,
            int from = -1)
        {
            m_tree = tree;
            m_hasShadowAttributes |= HasShadowAttributes(tree);

            int stylesheetElement = FindStylesheetElement(from, out bool simplified);

            if (m_stylesheetBaseUri is null)
            {
                // The principal module's outermost element settles the base URI every node the stylesheet
                // builds is relative to, xml:base there having had its say. It settles one more thing: a 1.0
                // stylesheet's implicit result tree is never inferred to be XHTML, that method and the meta
                // it writes being things 1.0 had no way to ask for and no way to decline.
                m_isPackage = IsXsltElement(stylesheetElement, out string outermostName)
                    && outermostName == "package";
                m_stylesheetBaseUri = StaticBaseUri(stylesheetElement);
                m_outputSettings.MayInferXhtml = !VersionOf(stylesheetElement).IsBackwardsCompatible;
            }

            if (simplified)
            {
                // The whole document element is the template body, so there is nothing else to gather.
                topLevel.Add((new ModuleElement(tree, stylesheetElement, true), uri));
                return;
            }

            if (ExcludedByUseWhen(stylesheetElement))
            {
                // A use-when on the stylesheet element cannot remove the element, there being no stylesheet
                // left to have written it. What it removes is everything inside.
                return;
            }

            // The outermost element is checked like any other. It is found rather than reached, so nothing
            // else would ever look at what it carries.
            m_scopeElement = stylesheetElement;
            _ = IsXsltElement(stylesheetElement, out string outermost);
            ValidateXsltElement(stylesheetElement, outermost, XsltPlacement.Subordinate);

            // What the module says about the annotations of the documents read, every module having a
            // say and two modules not being allowed to disagree.
            if (GetAttribute(stylesheetElement, "input-type-annotations") is string annotations)
            {
                NoteInputTypeAnnotations(annotations);
            }

            // A package's own version has to be one (§3.5.1). A used package's is read where it is used,
            // and the principal's would otherwise never be read at all — so a shadow attribute computing it
            // is evaluated here too, and what it computes, or fails to, is heard.
            if (outermost == "package"
                && GetAttribute(stylesheetElement, "package-version") is string declaredVersion
                && PackageVersion.TryParse(declaredVersion) is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{declaredVersion}' is not a package version. One is integers separated by dots, with a "
                    + "name after a hyphen if wanted: 2.0.5, or 3.10-alpha.");
            }

            // Only a package declares its modes, and only if it does not say otherwise. A plain
            // xsl:stylesheet has no such attribute and never did, so nothing changes for one.
            m_declaringModes[tree] = outermost == "package"
                && GetAttribute(stylesheetElement, "declared-modes")?.Trim()
                    is not "no" and not "false" and not "0";

            // The package's default-mode names a mode as surely as a template rule does, and a package
            // that declares its modes has to have declared that one.
            if (outermost == "package" && Implements30 && GetAttribute(stylesheetElement, "default-mode") is string defaulted)
            {
                int mode = DefaultModeIn(stylesheetElement);

                if (mode != CompiledStylesheet.DefaultMode)
                {
                    RequireDeclaredMode(mode, $"'{defaulted.Trim()}'", stylesheetElement);
                }
            }

            // use-when is answered here, at the first sight of a declaration: it decides whether the
            // declaration exists, so an xsl:include it excludes is never followed and a template it excludes
            // is never named.
            for (int child = FirstIncludedChild(stylesheetElement); child >= 0; child = NextIncludedSibling(child))
            {
                if (tree.KindOf(child) != NodeKind.Element)
                {
                    RequireNothingButSpace(tree, child);
                    continue;
                }

                m_tree = tree;
                if (IsXsltElement(child, out string localName) && localName == "include")
                {
                    m_scopeElement = child;
                    ValidateXsltElement(child, localName, XsltPlacement.Declaration);
                    (XdmTree included, string includedUri, int includedRoot) =
                        ResolveModule(child, uri, loading, "include");

                    loading.Add(includedUri);
                    GatherTopLevel(included, includedUri, topLevel, loading, includedRoot);
                    loading.Remove(includedUri);

                    m_tree = tree;
                    continue;
                }

                topLevel.Add((new ModuleElement(tree, child), uri));

                // What an xsl:override declares belongs to the package doing the overriding, not to the one
                // being overridden — so its children are spliced in here as top-level declarations of this
                // module and land at this module's precedence. Which is then all the overriding there is to
                // do: the used package's components came in lower, and a higher precedence already wins.
                if (localName == "use-package" && Implements30)
                {
                    foreach (int overriding in OverriddenDeclarations(child))
                    {
                        topLevel.Add((new ModuleElement(tree, overriding), uri));
                        m_overrideOf[new ModuleElement(tree, overriding)] = new ModuleElement(tree, child);
                    }
                }

            }
        }

        /// <summary>
        /// Whether a module writes any attribute in the shadow form.
        /// </summary>
        /// <remarks>
        /// One scan of the module's attributes rather than a second lookup on every attribute read: a
        /// stylesheet that uses none — which is nearly all of them — then pays a single field read wherever
        /// an attribute is simply absent, and the compiler's hot path is exactly those absences.
        /// </remarks>
        private static bool HasShadowAttributes(XdmTree tree)
        {
            for (int i = 0; i < tree.AttributeNodeCount; i++)
            {
                string local = tree.NameTable.GetLocalName(tree.FingerprintOf(XdmTree.AttributeIdBase + i));

                if (local.Length > 1 && local[0] == '_')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Records an <c>xsl:accumulator</c>, leaving its patterns and expressions for the pass that
        /// compiles them.
        /// </summary>
        /// <remarks>
        /// Declared in the first pass like a key, and for the same reason: a rule's expression may name
        /// another accumulator, and the name has to resolve whichever order the two were written in.
        /// </remarks>
        private void DeclareAccumulator(int element, ModuleElement source, int precedence)
        {
            string name = GetAttribute(element, "name")
                ?? throw new XsltException("An xsl:accumulator must have a name.");

            ExpandedName expanded = ResolveQualifiedName(element, name);

            // Import precedence settles it, as it does for a function or a template: two of one name at one
            // precedence is a stylesheet saying two different things about one accumulator, with no rule for
            // choosing — the values would differ at every node. Two at different precedences is a package
            // overriding one it used. And two at one precedence both overridden from above is the same
            // thing twice, so the clash is noted here and an error only once every module has been read.
            // An accumulator belongs to the package that declared it, like a key: the name is marked with the
            // package, so that two packages may each have one of the name.
            ExpandedName key = Scoped(expanded);

            if (m_accumulatorPrecedence.TryGetValue(key, out int declared))
            {
                if (declared == precedence)
                {
                    m_accumulatorClashes[key] = (precedence, name);
                    return;
                }

                if (declared > precedence)
                {
                    return;
                }
            }

            // The index is kept where it was, so an expression already compiled against the overridden
            // accumulator finds the overriding one at run time rather than a stale value.
            AccumulatorDefinition accumulator = new AccumulatorDefinition(
                expanded,
                m_accumulatorsByName.TryGetValue(key, out AccumulatorDefinition? replaced)
                    ? replaced.Index
                    : m_accumulators.Count)
            {
                Package = CurrentPackage,
            };

            if (replaced is null)
            {
                m_accumulators.Add(accumulator);
            }
            else
            {
                m_accumulators[accumulator.Index] = accumulator;
            }

            m_accumulatorsByName[key] = accumulator;
            m_accumulatorPrecedence[key] = precedence;
            m_pendingAccumulators.Add((accumulator, source with { Element = element }));
        }

        /// <summary>Compiles each accumulator's initial value and rules, after every name is known.</summary>
        private void CompileAccumulators()
        {
            foreach ((ExpandedName clashed, (int precedence, string name)) in m_accumulatorClashes)
            {
                if (m_accumulatorPrecedence[clashed] == precedence)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3350,
                        $"More than one xsl:accumulator is named '{name}' at the same import precedence, "
                        + "and nothing of higher precedence settles it.");
                }
            }

            foreach ((AccumulatorDefinition accumulator, ModuleElement source) in m_pendingAccumulators)
            {
                m_tree = source.Tree;
                m_scopeElement = source.Element;
                m_frameSlotCount = 0;

                accumulator.Type = ReadDeclaredType(source.Element);

                // Nothing here streams, so the attribute is not read further — but a value it may not take
                // is still refused, and a shadow attribute computes its value too late for the element table.
                if (GetAttribute(source.Element, "streamable") is string streamable)
                {
                    ValidateAttributeValue(source.Element, "streamable", streamable);
                }
                accumulator.InitialValue = RequireExpression(source.Element, "initial-value");

                List<AccumulatorRule> rules = new List<AccumulatorRule>();

                for (int child = FirstIncludedChild(source.Element); child >= 0;
                    child = NextIncludedSibling(child))
                {
                    if (m_tree.KindOf(child) != NodeKind.Element
                        || !IsXsltElement(child, out string localName)
                        || localName != "accumulator-rule")
                    {
                        continue;
                    }

                    rules.Add(CompileAccumulatorRule(child));
                }

                if (rules.Count == 0)
                {
                    // An accumulator with no rule keeps its initial value at every node of every document,
                    // which is a constant written the long way round.
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0010,
                        $"The accumulator '{accumulator.Name.LocalName}' has no xsl:accumulator-rule, so "
                        + "nothing would ever change its value.");
                }

                accumulator.Rules = rules.ToArray();
                accumulator.FrameSize = m_frameSlotCount;
                m_scopeElement = source.Element;
            }
        }

        /// <summary>Compiles one <c>xsl:accumulator-rule</c>, with <c>$value</c> in scope inside it.</summary>
        private AccumulatorRule CompileAccumulatorRule(int element)
        {
            m_scopeElement = element;

            string match = GetAttribute(element, "match")
                ?? throw new XsltException("An xsl:accumulator-rule must have a match pattern.");

            Pattern[] patterns = Pattern.Parse(match, this);
            bool atEnd = GetAttribute(element, "phase")?.Trim() == "end";

            // $value is what the accumulator was worth before this rule fired, which is what makes a rule a
            // rewriting rather than an assignment — and what lets "$value + 1" be a whole counter.
            int scopeMark = m_scope.Count;
            int slot = m_frameSlotCount++;
            m_scope.Add(new VariableBinding(new ExpandedName(string.Empty, "value"), slot, false));

            try
            {
                (Expr? select, Instruction[]? body) = CompileVariableValue(element);
                return new AccumulatorRule(patterns, atEnd, select, body, slot);
            }
            finally
            {
                m_scope.RemoveRange(scopeMark, m_scope.Count - scopeMark);
            }
        }

        /// <summary>
        /// Refuses a template rule in an <c>xsl:override</c> that does not name a mode which can be
        /// overridden.
        /// </summary>
        /// <remarks>
        /// A rule inside an override is redefining part of a <em>mode</em>, and a mode is a component only
        /// where it has a name. So <c>#all</c> names every mode and therefore no component; <c>#unnamed</c>
        /// names the one mode that is never a component; and <c>#default</c>, or saying nothing, comes to the
        /// unnamed mode unless a <c>default-mode</c> in scope says otherwise. The fourth clause is what makes
        /// this worth checking rather than obvious: the same rule written under
        /// <c>default-mode="x"</c> is perfectly good.
        /// </remarks>
        /// <param name="template">The <c>xsl:template</c> with a match pattern.</param>
        private void RequireOverridableMode(int template)
        {
            string[] tokens = (GetAttribute(template, "mode") ?? "#default")
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
            {
                tokens = new[] { "#default" };
            }

            foreach (string token in tokens)
            {
                bool unnamed = token switch
                {
                    "#all" or "#unnamed" => true,
                    "#default" => DefaultModeIn(template) == CompiledStylesheet.DefaultMode,
                    _ => false,
                };

                if (unnamed)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3440,
                        $"A template rule in an xsl:override names the mode '{token}', which is not a mode "
                        + "another package can have exposed. An overriding rule belongs to a named mode: "
                        + "'#all' names every mode and so no one component, and the unnamed mode is never "
                        + "a component at all.");
                }
            }
        }

        /// <summary>The declarations an <c>xsl:use-package</c>'s <c>xsl:override</c> children hold.</summary>
        private IEnumerable<int> OverriddenDeclarations(int usePackage)
        {
            for (int child = FirstIncludedChild(usePackage); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element
                    || !IsXsltElement(child, out string localName)
                    || localName != "override")
                {
                    continue;
                }

                for (int declared = FirstIncludedChild(child); declared >= 0;
                    declared = NextIncludedSibling(declared))
                {
                    if (m_tree.KindOf(declared) != NodeKind.Element)
                    {
                        if (m_tree.KindOf(declared) == NodeKind.Text
                            && !m_tree.StringValueOf(declared).AsSpan().IsWhiteSpace())
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE0010,
                                $"An xsl:override holds declarations, and '{m_tree.StringValueOf(declared).Trim()}' "
                                + "is text. Only whitespace may stand between them.");
                        }

                        continue;
                    }

                    // Overriding is redefining a component the used package offered, and only five kinds of
                    // declaration are components a package can offer. A key, an accumulator or a decimal
                    // format belongs to the package that declared it — there is no seam for another package
                    // to redefine it across.
                    if (!IsXsltElement(declared, out string kind)
                        || kind is not ("template" or "function" or "variable" or "param" or "attribute-set"))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0010,
                            $"'{QualifiedNameOf(declared)}' cannot stand in an xsl:override. Only a "
                            + "template, a function, a variable, a parameter or an attribute set is a "
                            + "component another package can redefine.");
                    }

                    if (kind == "template" && GetAttribute(declared, "match") is not null)
                    {
                        RequireOverridableMode(declared);
                    }

                    yield return declared;
                }
            }
        }

        /// <summary>
        /// Settles the static variables of a module and of everything it imports and includes, in
        /// stylesheet tree order (§9.7), before any module is loaded.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A static variable is in scope for what follows it in tree order, imports and includes expanded in
        /// place: a declaration after an xsl:import may read a static variable the imported module declared.
        /// Loading follows imports first and declarations after, which is the wrong order for that, so the
        /// statics are settled on a walk of their own that reads the modules in tree order, and the walk that
        /// loads them finds every static already settled.
        /// </para>
        /// <para>
        /// Import precedence is what settles two declarations of one name, and it is ranked here as loading
        /// ranks it: a module imported earlier is lower than one imported later, and every import lower than
        /// the module importing it. A module's rank is assigned when its walk ends, so a module still being
        /// walked — the principal, or an ancestor of the module being read — outranks every module that is
        /// finished, which is the one comparison a declaration ever needs to make.
        /// </para>
        /// </remarks>
        private void SettleStaticsOf(
            XdmTree tree,
            string? uri,
            HashSet<string> loading,
            XdmTree? includedIn,
            int from = -1)
        {
            XdmTree outer = m_tree;
            int scope = m_scopeElement;
            m_tree = tree;

            // Noted here rather than when the module is loaded, so that a shadow attribute on a static
            // declaration — _static="{$flag}", say — is read as one on this walk too.
            m_hasShadowAttributes |= HasShadowAttributes(tree);

            if (includedIn is not null)
            {
                m_includedIn[tree] = includedIn;
            }

            try
            {
                int root = FindStylesheetElement(from, out bool simplified);

                if (simplified || ExcludedByUseWhen(root))
                {
                    return;
                }

                for (int child = FirstIncludedChild(root); child >= 0; child = NextIncludedSibling(child))
                {
                    if (tree.KindOf(child) != NodeKind.Element || !IsXsltElement(child, out string localName))
                    {
                        continue;
                    }

                    if (localName is "import" or "include")
                    {
                        m_scopeElement = child;
                        (XdmTree module, string moduleUri, int moduleRoot) =
                            ResolveModule(child, uri, loading, localName);

                        loading.Add(moduleUri);
                        SettleStaticsOf(
                            module, moduleUri, loading, localName == "include" ? tree : null, moduleRoot);
                        loading.Remove(moduleUri);

                        if (localName == "import")
                        {
                            m_staticRanks[module] = ++m_staticRankCounter;
                        }

                        m_tree = tree;
                        continue;
                    }

                    if (localName is "variable" or "param" && ReadDeclarationFlag(child, "static"))
                    {
                        SettleStatic(child);
                        m_tree = tree;
                    }
                }
            }
            finally
            {
                m_tree = outer;
                m_scopeElement = scope;
            }
        }

        /// <summary>
        /// Answers every <c>use-when</c> inside an element, in document order, and remembers each answer.
        /// </summary>
        /// <remarks>
        /// An element the attribute removes takes its descendants with it, so the walk stops there — what is
        /// inside a removed element is never read at all, which is the whole point of the attribute.
        /// </remarks>
        /// <param name="element">The element whose content to walk.</param>
        private void SettleUseWhenIn(int element)
        {
            for (int child = m_tree.FirstChildOf(element); child >= 0; child = m_tree.NextSiblingOf(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element && !ExcludedByUseWhen(child))
                {
                    SettleUseWhenIn(child);
                }
            }
        }

        /// <summary>The import rank of each module whose static walk has finished.</summary>
        private readonly Dictionary<XdmTree, int> m_staticRanks = new();

        /// <summary>The module each included module was included by, whose rank it shares.</summary>
        private readonly Dictionary<XdmTree, XdmTree> m_includedIn = new();

        private int m_staticRankCounter;

        /// <summary>What each static variable's standing declaration was, for the consistency rule.</summary>
        private readonly Dictionary<ExpandedName, StaticDeclaration> m_staticDeclarations = new();

        private readonly record struct StaticDeclaration(XdmTree Tree, int Element, bool IsParameter, bool Initialized);

        /// <summary>A module's import rank; the highest of all while its walk is still going.</summary>
        private int StaticRankOf(XdmTree tree)
        {
            while (m_includedIn.TryGetValue(tree, out XdmTree? includer))
            {
                tree = includer;
            }

            return m_staticRanks.TryGetValue(tree, out int rank) ? rank : int.MaxValue;
        }

        /// <summary>Reads a <c>static</c> declaration's name and settles its value.</summary>
        private void SettleStatic(int element)
        {
            int scope = m_scopeElement;
            m_scopeElement = element;

            try
            {
                string name = GetAttribute(element, "name")
                    ?? throw new XsltException("A static xsl:variable or xsl:param must have a name.");

                DeclareStatic(element, ResolveQualifiedName(element, name), name, LocalNameOf(element) == "param");
            }
            finally
            {
                m_scopeElement = scope;
            }
        }

        /// <summary>
        /// Refuses text between the declarations of a stylesheet.
        /// </summary>
        /// <remarks>
        /// Only whitespace may stand there, and what a stylesheet means by anything else is anybody's guess:
        /// text at the top level does nothing, produces nothing, and is almost always a tag that was meant to
        /// be a declaration and is not one. The specification calls it <c>XTSE0120</c>.
        /// </remarks>
        private static void RequireNothingButSpace(XdmTree tree, int node)
        {
            if (tree.KindOf(node) != NodeKind.Text || tree.StringValueOf(node).AsSpan().IsWhiteSpace())
            {
                return;
            }

            throw XsltErrors.Error(
                XsltErrorCode.XTSE0120,
                $"A stylesheet holds declarations, and '{tree.StringValueOf(node).Trim()}' is text. Only "
                + "whitespace may stand between them.");
        }

        private void LoadReferencedModule(int element, string? baseUri, HashSet<string> loading, string kind)
        {
            (XdmTree imported, string importedUri, int importedRoot) =
                ResolveModule(element, baseUri, loading, kind);

            // What an xsl:import or xsl:include brings in is a stylesheet module. A package is not one: it
            // is a unit of its own, reached through xsl:use-package, and importing it would make its
            // declarations this package's at some precedence, which is not what a package is for.
            XdmTree outer = m_tree;
            m_tree = imported;
            int outermost = FindStylesheetElement(importedRoot, out bool simplified);
            bool isPackage = !simplified && IsXsltElement(outermost, out string root) && root == "package";
            m_tree = outer;

            if (isPackage)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0165,
                    $"The module an xsl:{kind} refers to is an xsl:package, and only a stylesheet module can "
                    + $"be {(kind == "import" ? "imported" : "included")}. A package is used, through "
                    + "xsl:use-package.");
            }

            loading.Add(importedUri);
            m_importDepth += kind == "import" ? 1 : 0;

            try
            {
                LoadModule(imported, importedUri, loading, importedRoot);
            }
            finally
            {
                m_importDepth -= kind == "import" ? 1 : 0;
                loading.Remove(importedUri);
            }
        }

        /// <summary>
        /// Loads the package an <c>xsl:use-package</c> names, at a lower import precedence.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A used package behaves here exactly as an imported module does: its components land at a lower
        /// precedence, so anything the using package declares under the same name wins. That is not a
        /// simplification of the specification but the shape of it — the difference between the two is what
        /// each package is <em>allowed</em> to see of the other, which is visibility, rather than how the
        /// declarations combine once seen.
        /// </para>
        /// <para>
        /// The name is an identity rather than a location, so it goes to a resolver of its own; see
        /// <see cref="XsltOptions.PackageResolver"/>.
        /// </para>
        /// </remarks>
        private void LoadUsedPackage(int element, HashSet<string> loading)
        {
            string name = GetAttribute(element, "name")?.Trim()
                ?? throw new XsltException("An xsl:use-package must have a name.");

            // Which versions of it will do. Absent, any; written, a range in the grammar of §3.5.2, and a
            // text that is not one is refused where it stands.
            string? wantedVersions = GetAttribute(element, "package-version");
            PackageVersionRange range = wantedVersions is null
                ? PackageVersionRange.Any
                : PackageVersionRange.TryParse(wantedVersions)
                    ?? throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"'{wantedVersions}' is not a package version range. One is a version such as 2.0.5 "
                        + "or 3.10-alpha, a prefix such as 1.3.*, a bound such as 1.3+ or to 4.0, a span such "
                        + "as 1 to 5, any of those separated by commas, or *.");

            if (m_options.PackageResolver is null)
            {
                throw new XsltException(
                    $"This package uses xsl:use-package name=\"{name}\", but no package resolver was "
                    + "configured. Set XsltOptions.PackageResolver to allow packages to be found.");
            }

            // A resolver that knows its versions is asked for them, and the highest the range takes is the
            // one loaded — the specification leaves the choice to the processor, and the latest is what a
            // caller naming a range means. One that knows none supplies what it has, and what it declares
            // is checked against the range once read.
            string? chosen = range.Best(m_options.PackageResolver as IXsltPackageResolver, name, out bool offered);

            if (offered && chosen is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE3000,
                    $"No version of the package '{name}' that this xsl:use-package takes "
                    + $"(package-version=\"{wantedVersions}\") could be found.");
            }

            // Used once however often it is named: a package is a thing, not a text to be spliced in, so
            // naming it twice cannot mean two copies of it. What each naming keeps is its own: which package
            // did the naming, and what that package's xsl:accept children said about what it takes. A
            // package is its name and its version, so two versions of one name are two packages.
            string identity = chosen is null ? name : name + " " + chosen;
            bool alreadyLoaded = m_packageIdByName.TryGetValue(identity, out int id);

            if (!alreadyLoaded)
            {
                id = m_packageCount + 1;
            }

            RecordUse(element, id);

            if (alreadyLoaded)
            {
                return;
            }

            ResolvedResource resolved = m_options.PackageResolver.Resolve(name, chosen)
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XTSE3000,
                    $"No package named '{name}' could be found.");

            XdmTree package;

            try
            {
                package = XdmTreeBuilder.FromXml(
                    resolved.Reader, locations: true, entityResolver: m_options.EntityResolver, baseUri: resolved.Uri);
            }
            finally
            {
                resolved.Reader.Dispose();
            }

            XdmTree outer = m_tree;
            int outerPackage = m_package;
            loading.Add(name);

            // The one boundary a flattened compilation still has to keep. Everything else about a used
            // package behaves as an import does; what it offers, and therefore what its own xsl:expose is
            // about, is its own affair and not the using package's.
            m_package = ++m_packageCount;
            m_packageIdByName[identity] = m_package;

            try
            {
                // Its static variables first, as for the principal: they are the package's own, keyed by it.
                SettleStaticsOf(package, resolved.Uri, loading, null);
                LoadModule(package, resolved.Uri, loading);

                if (chosen is null)
                {
                    RequireVersionInRange(package, name, range, wantedVersions);
                }
            }
            finally
            {
                loading.Remove(name);
                m_tree = outer;
                m_package = outerPackage;
            }
        }

        /// <summary>
        /// Checks that a package a resolver supplied without being asked for a version is one the
        /// <c>xsl:use-package</c> takes, by what the package declares of itself.
        /// </summary>
        /// <remarks>
        /// A package declaring no version is version 1, the specification says; one declaring something
        /// that is not a version is read the same way, the attribute not being checked for its own sake —
        /// the suite writes shadow attributes there, whose static expressions are nobody's business until
        /// the version is asked for.
        /// </remarks>
        private void RequireVersionInRange(XdmTree package, string name, PackageVersionRange range, string? wanted)
        {
            if (wanted is null)
            {
                return;
            }

            m_tree = package;
            int outermost = FindStylesheetElement(out _);
            string? declared = GetAttribute(outermost, "package-version");
            PackageVersion version = (declared is null ? null : PackageVersion.TryParse(declared))
                ?? PackageVersion.TryParse("1")!;

            if (!range.Matches(version))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE3000,
                    $"The package '{name}' found is version {version.Written}, and this xsl:use-package "
                    + $"takes package-version=\"{wanted}\".");
            }
        }

        /// <summary>
        /// Records that the module being read uses a package: which one, through which element, and what
        /// the element's <c>xsl:accept</c> children say about what it takes.
        /// </summary>
        /// <remarks>
        /// Recorded before the load, because the load moves <c>m_tree</c> onto the package being read and
        /// these elements are in the module doing the using — and recorded again for a package already
        /// loaded, because a second package naming it is a second relationship with its own acceptances.
        /// </remarks>
        /// <param name="usePackage">The <c>xsl:use-package</c> element.</param>
        /// <param name="used">The package it names.</param>
        private void RecordUse(int usePackage, int used)
        {
            m_usePackageIds[new ModuleElement(m_tree, usePackage)] = used;

            if (!m_uses.TryGetValue(m_package, out List<int>? uses))
            {
                m_uses[m_package] = uses = new List<int>();
            }

            uses.Add(used);

            for (int child = FirstIncludedChild(usePackage); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string accepting)
                    && accepting == "accept")
                {
                    m_acceptances.Add(new Acceptance(new ModuleElement(m_tree, child), used, m_package));
                }
            }
        }

        /// <summary>
        /// Resolves an <c>href</c> to a parsed module, refusing when no resolver was configured and when a
        /// reference would form a cycle.
        /// </summary>
        private (XdmTree Tree, string Uri, int Root) ResolveModule(
            int element,
            string? baseUri,
            HashSet<string> loading,
            string kind)
        {
            string href = GetAttribute(element, "href")
                ?? throw new XsltException($"An xsl:{kind} must have an href.");

            // A reference may name an element inside the document rather than the document itself, which
            // is how a stylesheet is embedded in something that is not one. The fragment is a bare name,
            // the identifier of the element to start at; the document is fetched by what is in front of it.
            int hash = href.IndexOf('#', StringComparison.Ordinal);
            string? fragment = hash < 0 ? null : href[(hash + 1)..];

            if (fragment is not null)
            {
                if (!PackageVersion.IsNcName(fragment))
                {
                    throw new XsltException(
                        $"An xsl:{kind} names a fragment of '{href[..hash]}' by a form this engine does "
                        + "not follow: only a bare name, which names the element with that identifier.");
                }

                href = href[..hash];
            }

            // What the reference resolves against is the base URI of the element that wrote it, not the
            // module as a whole: an xml:base above it moves it, and so does having been read out of an
            // external entity, which is relative to where the entity was and not to where it was used.
            baseUri = XPath.Xpath2FunctionExpr.BaseUriOf(m_tree, element, baseUri) ?? baseUri;

            if (m_options.StylesheetResolver is null)
            {
                throw new XsltException(
                    $"This stylesheet uses xsl:{kind} href=\"{href}\", but no stylesheet resolver was "
                    + "configured. Set XsltOptions.StylesheetResolver to allow references to be followed.");
            }

            // Asked once per reference, however many walks pass it. The static variables of a module are
            // settled on a first walk, before the module is loaded, and both walks follow every import; a
            // resolver reading from the network would otherwise fetch each module twice. The reference as
            // written, with what it resolves against, names one resource — a resolver answers the same
            // identity for the same stylesheet every time, which is what ResolvedResource asks of it.
            string reference = string.Concat(baseUri, "\n", href, "\n", fragment);

            if (m_references.TryGetValue(reference, out (XdmTree Tree, string Uri, int Root) known))
            {
                RequireNotBeingRead(loading, known.Uri, href);
                return known;
            }

            ResolvedResource? resolved = m_options.StylesheetResolver.Resolve(href, baseUri)
                ?? throw new XsltException($"The stylesheet 'xsl:{kind} href=\"{href}\"' could not be found.");

            try
            {
                RequireNotBeingRead(loading, resolved.Uri, href);

                // And parsed once, however many references name it: two spellings of one module are one tree.
                if (!m_moduleCache.TryGetValue(resolved.Uri, out XdmTree? tree))
                {
                    tree = XdmTreeBuilder.FromXml(
                        resolved.Reader, locations: true, entityResolver: m_options.EntityResolver, baseUri: resolved.Uri);
                    m_moduleCache[resolved.Uri] = tree;
                }

                known = (tree, resolved.Uri, fragment is null ? -1 : NamedElement(tree, fragment, href, kind));
            }
            finally
            {
                resolved.Reader.Dispose();
            }

            m_references[reference] = known;
            return known;
        }


        /// <summary>The element of a document carrying an identifier, for a reference that named one.</summary>
        /// <remarks>
        /// An identifier is an attribute the document type declared as an ID, so a document with no
        /// declaration has no identifiers and nothing to find. That is why the suite embeds a stylesheet
        /// under an internal subset saying which attribute is the ID.
        /// </remarks>
        /// <param name="tree">The document read.</param>
        /// <param name="fragment">The identifier asked for.</param>
        /// <param name="href">The reference as written, for the message.</param>
        /// <param name="kind">Whether it was an import or an include, for the message.</param>
        private static int NamedElement(XdmTree tree, string fragment, string href, string kind)
        {
            if (IdExpr.BuildIndex(tree).TryGetValue(fragment, out int element))
            {
                return element;
            }

            throw new XsltException(
                $"An xsl:{kind} names '{fragment}' in '{href}', and nothing in that document carries "
                + "that identifier.");
        }

        /// <summary>Refuses a reference to a module that is still being read above it, which is a cycle.</summary>
        private static void RequireNotBeingRead(HashSet<string> loading, string uri, string href)
        {
            if (loading.Contains(uri))
            {
                throw new XsltException(
                    $"The stylesheet reference '{href}' forms a cycle: '{uri}' is already being read.");
            }
        }

        /// <summary>The modules read so far, by the URI they resolved to.</summary>
        private readonly Dictionary<string, XdmTree> m_moduleCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// What each reference resolved to, by the reference as written together with its base URI.
        /// </summary>
        private readonly Dictionary<string, (XdmTree Tree, string Uri, int Root)> m_references = new(StringComparer.Ordinal);

        /// <summary>
        /// Records one top-level declaration, without compiling any bodies yet, so that later references to it
        /// resolve.
        /// </summary>
        private void DeclareTopLevelElement(ModuleElement source, int precedence)
        {
            int child = source.Element;

            if (source.Simplified)
            {
                DeclareSimplifiedTemplate(source, precedence);
                return;
            }

            if (!IsXsltElement(child, out string localName))
            {
                // A top-level element in some other namespace is a stylesheet's own data, which the
                // specification allows and this engine passes over. One in no namespace is neither that nor a
                // declaration, and is almost always a tag that was meant to carry a prefix.
                if (m_tree.NameTable.GetNamespaceUri(m_tree.FingerprintOf(child)).Length == 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0130,
                        $"'{QualifiedNameOf(child)}' stands among the declarations of a stylesheet and is in "
                        + "no namespace. A stylesheet's own data elements have to be in one, so that they "
                        + "cannot be mistaken for declarations a later version might define.");
                }

                return;
            }

            {
                m_scopeElement = child;
                ValidateXsltElement(child, localName, XsltPlacement.Declaration);

                switch (localName)
                {
                    case "template":
                        DeclareTemplate(child, source, precedence);
                        break;

                    case "variable":
                    case "param":
                        DeclareGlobal(child, source, precedence);
                        break;

                    case "key":
                        DeclareKey(child, source);
                        break;

                    case "output":
                        ReadOutputSettings(child, precedence);
                        break;

                    case "strip-space":
                    case "preserve-space":
                        ReadWhitespaceControl(child, strip: localName == "strip-space", precedence);
                        break;

                    case "decimal-format":
                        ReadDecimalFormat(child, precedence);
                        break;

                    case "attribute-set":
                        DeclareAttributeSet(child, source, precedence);
                        break;

                    case "expose" when Implements30:
                        m_exposures.Add((source with { Element = child }, m_package));
                        break;

                    case "namespace-alias":
                        ReadNamespaceAlias(child, precedence);
                        break;

                    case "accumulator" when Implements30:
                        DeclareAccumulator(child, source, precedence);
                        break;

                    case "mode" when Implements30:
                        ReadMode(child, precedence);
                        break;

                    case "global-context-item" when Implements30:
                        m_globalContextItems.Add(
                            (CurrentPackage, m_tree, ReadContextItem(child, forStylesheet: true)));
                        break;

                    case "function":
                        DeclareFunction(child, source, precedence);
                        break;

                    case "character-map":
                        DeclareCharacterMap(child, source, precedence);
                        break;

                    case "import-schema":
                        // Imported before any declaration was read, where the processor is schema-aware.
                        // Otherwise not in the ignorable set below: a stylesheet importing a schema is
                        // written against the types in it, and running it as though the import were absent
                        // would silently give back untyped answers to typed questions — the same reason
                        // validation="strict" is refused rather than shrugged off.
                        if (m_schemas is not null)
                        {
                            break;
                        }

                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1650,
                            "xsl:import-schema needs a schema-aware processor, and this one was not asked to "
                            + "be: set XsltOptions.SchemaAware.");

                    default:
                        // Declarations this engine does not implement are ignored rather than rejected, so a
                        // stylesheet using them still runs to the extent that it can.
                        break;
                }
            }
        }

        /// <summary>
        /// Records an <c>xsl:function</c> and its shape, without compiling the body.
        /// </summary>
        /// <remarks>
        /// Declared in the first pass like a template, so that a function may call one written after it — and
        /// so that it may call itself, which is how anything recursive is expressed in a language with no
        /// loops.
        /// </remarks>
        private void DeclareFunction(int element, ModuleElement source, int precedence)
        {
            string name = GetAttribute(element, "name")
                ?? throw new XsltException("An xsl:function must have a name.");

            if (name.IndexOf(':') < 0)
            {
                throw new XsltException(
                    $"The function name '{name}' has no prefix. A function declared by a stylesheet must be "
                    + "in a namespace, so that it can never shadow one from the core library.");
            }

            ExpandedName expanded = ResolveQualifiedName(element, name);

            // override-extension-function is 3.0's spelling of override, the old name having been a poor one:
            // what it says is whether this function is preferred to an extension function of the same name,
            // and nothing about overriding another stylesheet function. A stylesheet may write both, which
            // is how one serves a 2.0 processor and a 3.0 one at once; what it may not do is write two
            // different answers, there being no rule for which of them it meant.
            if (GetAttribute(element, "override") is not null
                && GetAttribute(element, "override-extension-function") is not null
                && ReadDeclarationFlag(element, "override")
                    != ReadDeclarationFlag(element, "override-extension-function"))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"The function '{name}' says both override and override-extension-function, and says "
                    + "opposite things. They are two names for one attribute, 3.0 having renamed it.");
            }

            int arity = 0;

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string localName) && localName == "param")
                {
                    arity++;
                }
            }

            // A function obeys import precedence like everything else declared at the top level. Two of one
            // name and arity at one precedence is a stylesheet saying the same thing twice with nothing to
            // decide between them; two at different precedences is a module overriding a function it
            // imported, which is ordinary. Modules are read in increasing precedence, so what arrives second
            // is the one that wins.
            if (m_functionPrecedence.TryGetValue((expanded, arity), out int declared))
            {
                if (declared == precedence)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0770,
                        $"More than one xsl:function is named '{name}' with {arity} parameter(s), in the "
                        + "same module or in two of the same import precedence. Only an import can override "
                        + "a function.");
                }

                if (declared > precedence)
                {
                    // Unreachable by the load order, and left standing rather than asserted: the body is
                    // still compiled below, because a function nothing can call is still one the stylesheet
                    // has to have written correctly.
                    m_pendingFunctions.Add((new UserFunction(expanded, arity), source with { Element = element }));
                    return;
                }

            }

            // What the declaration said of its visibility is what xsl:evaluate goes by, an xsl:expose
            // having its say below through the component; a function that said nothing is private. The
            // one being replaced stays reachable as xsl:original, which is how an xsl:override says "what
            // it used to do, and then this" — kept on the function rather than under a shared name, since
            // two overrides in one package each mean their own original.
            UserFunction function = new UserFunction(expanded, arity)
            {
                Visibility = DeclaredVisibility(element) ?? Visibility.Private,
                Original = m_functions.TryGetValue((expanded, arity), out UserFunction? replaced) ? replaced : null,
            };

            m_functions[(expanded, arity)] = function;
            m_functionPrecedence[(expanded, arity)] = precedence;
            m_pendingFunctions.Add((function, source with { Element = element }));

            PackageComponent component = Component(
                "function", expanded, arity, element, visibility => function.Visibility = visibility);

            component.Function = function;
            m_components.Add(component);
        }

        /// <summary>Compiles the bodies of the declared functions, after every name is known.</summary>
        private void CompileFunctions()
        {
            foreach ((UserFunction function, ModuleElement source) in m_pendingFunctions)
            {
                m_tree = source.Tree;
                m_scopeElement = source.Element;
                m_frameSlotCount = 0;
                m_compilingFunction = function;

                int scopeDepth = m_scope.Count;
                List<int> slots = new List<int>(function.Arity);
                List<XdmSequenceType?> parameterTypes = new List<XdmSequenceType?>(function.Arity);
                function.ResultType = ReadDeclaredType(source.Element);
                function.IsAbstract = ReadVisibility(source.Element) == Visibility.Abstract;

                // new-each-time="no" promises that the same arguments give the very same nodes, and
                // cache="yes" asks for the memoization outright. Either way a call may be answered from what
                // an earlier one returned; "maybe", the default, leaves it open and is read as "no promise",
                // so nothing is remembered and every call builds afresh.
                function.Deterministic =
                    ReadNewEachTime(source.Element) == false
                    || (GetAttribute(source.Element, "cache") is not null
                        && ReadDeclarationFlag(source.Element, "cache"));

                for (int child = FirstIncludedChild(source.Element); child >= 0;
                    child = NextIncludedSibling(child))
                {
                    if (m_tree.KindOf(child) != NodeKind.Element
                        || !IsXsltElement(child, out string localName) || localName != "param")
                    {
                        continue;
                    }

                    string parameterName = GetAttribute(child, "name")
                        ?? throw new XsltException("An xsl:param must have a name.");

                    if (ReadDeclarationFlag(child, "tunnel"))
                    {
                        throw new XsltException(
                            $"The parameter '{parameterName}' of function "
                            + $"'{function.Name.LocalName}()' is declared tunnel=\"yes\". Only a template "
                            + "parameter can be a tunnel parameter; a function takes its arguments and "
                            + "nothing else.");
                    }

                    // Every parameter of a function is required already — a call supplies one argument for
                    // each, or it calls a different function — so 3.0 admits the attribute only as a
                    // restatement of that, and 2.0 does not admit it at all.
                    if (GetAttribute(child, "required") is not null)
                    {
                        if (!Implements30)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE0090,
                                $"The parameter '{parameterName}' of function "
                                + $"'{function.Name.LocalName}()' is declared required. XSLT 2.0 has no such "
                                + "attribute on a function's parameter: every one of them is required.");
                        }

                        if (!ReadDeclarationFlag(child, "required"))
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE0020,
                                $"The parameter '{parameterName}' of function "
                                + $"'{function.Name.LocalName}()' is declared required=\"no\". Every parameter "
                                + "of a function is required, so the only thing the attribute can say is so.");
                        }
                    }

                    int slot = m_frameSlotCount++;
                    slots.Add(slot);
                    parameterTypes.Add(ReadDeclaredType(child));
                    m_scope.Add(new VariableBinding(ResolveQualifiedName(child, parameterName), slot, false));
                }

                m_scopeElement = source.Element;
                function.ParameterSlots = slots.ToArray();
                function.ParameterTypes = parameterTypes.ToArray();
                function.Body = CompileSequenceSkippingParameters(source.Element);
                function.BaseUri = StaticBaseUri(source.Element);
                function.FrameSize = m_frameSlotCount;

                // A body that is nothing but xsl:sequence returns that value as it is, rather than as the
                // text of a tree built around it.
                if (function.Body.Length == 1 && function.Body[0] is SequenceInstruction { Select: not null } only)
                {
                    function.DirectResult = only.Select;

                    // A call that is the whole of the value — the call itself, or a branch of an if or the
                    // return of a let that is — can be made in place of this invocation once the body has
                    // returned. That is what lets a function recurse for as long as it likes, provided the
                    // recursion is the last thing it does.
                    function.DirectResult.MarkTailPosition();
                }

                m_scope.RemoveRange(scopeDepth, m_scope.Count - scopeDepth);
            }

            m_compilingFunction = null;
        }

        /// <summary>Compiles a function body, which is everything after its parameters.</summary>
        private Instruction[] CompileSequenceSkippingParameters(int element)
        {
            return CompileSequence(element, name => name == "param");
        }

        /// <summary>
        /// Declares the single implicit template of a simplified stylesheet, matching the root.
        /// </summary>
        private void DeclareSimplifiedTemplate(ModuleElement source, int precedence)
        {
            m_scopeElement = source.Element;

            Pattern[] patterns = Pattern.Parse("/", this);
            Template template = new Template(m_pendingTemplates.Count, patterns, null, null)
            {
                ImportPrecedence = precedence,
                ImportFloor = m_importFloor,
            };

            m_rules.Add(new TemplateRule(
                patterns[0], template, patterns[0].Priority, CompiledStylesheet.DefaultMode));
            m_pendingTemplates.Add((template, source));
        }

        private void DeclareTemplate(int element, ModuleElement source, int precedence)
        {
            string? match = GetAttribute(element, "match");
            string? name = GetAttribute(element, "name");

            if (match is null && name is null)
            {
                throw new XsltException("An xsl:template must have a match pattern, a name, or both.");
            }

            ExpandedName? templateName = name is null ? null : ResolveQualifiedName(element, name);
            string? written = GetAttribute(element, "mode");
            (int[] modes, bool everyMode) = ResolveModeList(element, written);

            // Only a template rule belongs to a mode. A named template belongs to none, so it neither needs
            // a mode declared nor says anything about which modes the package uses — which is what makes the
            // check about 'match' rather than about xsl:template.
            if (match is not null && !everyMode)
            {
                foreach (int mode in modes)
                {
                    RequireDeclaredMode(mode, written is null ? "it does not name" : $"'{written}'", element);
                    NoteImplicitMode(mode, element);
                }
            }

            double? priority = null;
            string? priorityText = GetAttribute(element, "priority");
            if (priorityText is not null)
            {
                double parsed = XPathValue.ParseNumber(priorityText);
                if (double.IsNaN(parsed))
                {
                    throw new XsltException($"'{priorityText}' is not a valid template priority.");
                }

                priority = parsed;
            }

            Template template = new Template(
                m_pendingTemplates.Count, Array.Empty<Pattern>(), templateName, priority)
            {
                ImportPrecedence = precedence,
                ImportFloor = m_importFloor,
                Visibility = ReadVisibility(element),
                ResultType = ReadDeclaredType(element),
            };

            if (templateName is not null)
            {
                m_components.Add(Component(
                    "template",
                    templateName.Value,
                    -1,
                    element,
                    visibility => template.Visibility = visibility));
            }

            if (templateName is not null)
            {
                // A stylesheet may deliberately replace a template it imported, so a name clash is only an
                // error when neither declaration outranks the other.
                if (m_namedTemplates.TryGetValue(templateName.Value, out Template? existing))
                {
                    if (existing.ImportPrecedence == precedence)
                    {
                        throw new XsltException($"More than one template is named '{name}'.");
                    }

                    if (existing.ImportPrecedence < precedence)
                    {
                        // The one being replaced is remembered on the one replacing it, which is how an
                        // xsl:override says "what it used to do, and then this". Kept per template rather
                        // than under a shared name: an override of an override reaches the override it
                        // replaced, not the bottom of the chain, and two overrides in one package must not
                        // see each other's.
                        template.Original = existing;
                        m_namedTemplates[templateName.Value] = template;
                    }
                }
                else
                {
                    m_namedTemplates.Add(templateName.Value, template);
                }
            }

            if (match is not null)
            {
                // The pattern itself is read once every declaration is in, since it may name a function or
                // a key declared further down; the rules it makes are added there too.
                m_pendingMatches.Add((template, m_tree, element, match, modes, everyMode, priority));
            }

            m_pendingTemplates.Add((template, source));
        }

        /// <summary>Every template rule's match attribute, waiting for the declarations to be complete.</summary>
        private readonly List<(
            Template Template,
            XdmTree Tree,
            int Element,
            string Match,
            int[] Modes,
            bool EveryMode,
            double? Priority)> m_pendingMatches = new();

        /// <summary>
        /// Reads the match pattern of every template rule, and records the rules it makes.
        /// </summary>
        /// <remarks>
        /// A pattern is an expression like any other and may call a stylesheet function, use a key or read a
        /// global variable — any of which the stylesheet is free to declare after the template that uses it.
        /// So this waits for the whole of the stylesheet, as an <c>xsl:key</c>'s own pattern does.
        ///
        /// A union pattern makes several rules and not one, each with the default priority of its own
        /// alternative (XSLT 3.0 §6.4), which is what lets an <c>xsl:next-match</c> go from one alternative
        /// to the next. A priority written on the template applies to all of them, and then there is nothing
        /// left to tell them apart: they are one rule again.
        /// </remarks>
        private void CompileTemplateMatches()
        {
            foreach ((Template template, XdmTree tree, int element, string match, int[] modes, bool everyMode,
                double? priority) in m_pendingMatches)
            {
                m_tree = tree;
                m_scopeElement = element;
                m_frameSlotCount = 0;

                Pattern[] patterns = Pattern.Parse(match, this);
                template.Patterns = patterns;

                if (everyMode)
                {
                    // Held back further: mode="#all" covers every mode there turns out to be, and a mode
                    // comes into existence the first time anything names it — which may be in a template
                    // body not yet compiled.
                    m_everyModeTemplates.Add((template, patterns, priority));
                    continue;
                }

                foreach (Pattern pattern in patterns)
                {
                    foreach (int mode in modes)
                    {
                        m_rules.Add(new TemplateRule(pattern, template, priority ?? pattern.Priority, mode));
                    }
                }
            }
        }

        /// <summary>
        /// Adds a rule in every mode for each template declared <c>mode="#all"</c>.
        /// </summary>
        /// <remarks>
        /// Run once the whole stylesheet is compiled, when the set of modes is complete: a mode exists because
        /// something named it, and the last thing to name one may be an <c>xsl:apply-templates</c> deep in a
        /// template body. Expanding here rather than matching "any mode" at run time keeps dispatch exactly as
        /// it was — one bucket per mode, no extra list to merge on the hot path.
        /// </remarks>
        private void ExpandEveryModeTemplates()
        {
            foreach ((Template template, Pattern[] patterns, double? priority) in m_everyModeTemplates)
            {
                foreach (Pattern pattern in patterns)
                {
                    m_rules.Add(new TemplateRule(
                        pattern, template, priority ?? pattern.Priority, CompiledStylesheet.DefaultMode));

                    foreach (int mode in m_modes.Values)
                    {
                        m_rules.Add(new TemplateRule(pattern, template, priority ?? pattern.Priority, mode));
                    }
                }
            }
        }

        private void DeclareGlobal(int element, ModuleElement source, int precedence)
        {
            string? name = GetAttribute(element, "name")
                ?? throw new XsltException("A global xsl:variable or xsl:param must have a name.");

            if (ReadDeclarationFlag(element, "tunnel"))
            {
                throw new XsltException(
                    $"The top-level {QualifiedNameOf(element)} '{name}' is declared tunnel=\"yes\". Only a "
                    + "template parameter can be a tunnel parameter; a global is already visible everywhere.");
            }

            bool isParameter = LocalNameOf(element) == "param";
            bool required = ReadDeclarationFlag(element, "required");

            if (required && (GetAttribute(element, "select") is not null || HasContent(element)))
            {
                throw new XsltException(
                    $"The top-level {QualifiedNameOf(element)} '{name}' is declared required=\"yes\" and also "
                    + "given a default. A required parameter is one the caller must supply, so a default "
                    + "could never be used.");
            }

            ExpandedName expanded = ResolveQualifiedName(element, name);

            // Two of one name at one precedence is a stylesheet declaring the same thing twice, and nothing
            // decides which wins. Two at different precedences is ordinary: that is what importing a module
            // and overriding part of it looks like, and the higher one shadows the other.
            if (!m_declaredGlobals.Add((expanded, precedence)))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0630,
                    $"'{name}' is declared twice as a global, in the same module or in two of the same "
                    + "import precedence. Only an import can override a global.");
            }

            // Already settled, during the walk that gathered these declarations — a static variable has to be
            // worth something before the use-when below it is answered, which is long before here. All that
            // is left for it is the duplicate check above, which is the same for every global.
            if (Implements30 && ReadDeclarationFlag(element, "static"))
            {
                return;
            }

            // The highest precedence a non-static declaration of the name has, for a static variable of
            // the name to be shadowed by it where it is the higher (§9.7: precedence settles which is used).
            m_globalPrecedence[Scoped(expanded)] = Math.Max(m_globalPrecedence.GetValueOrDefault(Scoped(expanded), -1), precedence);

            int slot = m_globalSlotCount++;

            // Declarations are recorded in increasing precedence, and variable lookup searches backwards, so a
            // higher-precedence definition of the same name naturally shadows the one it imported.
            m_scope.Add(new VariableBinding(expanded, slot, true));

            GlobalVariable global = new GlobalVariable(
                expanded, slot, null, null, isParameter, required, ReadDeclaredType(element))
            {
                IsAbstract = ReadVisibility(element) == Visibility.Abstract,
            };

            m_globals.Add(global);
            m_globalElements.Add(source);

            if (IsOverriding(element))
            {
                TakeOverSlot(global, source);
            }

            // A parameter is a variable to whoever refers to it, and not a component to xsl:expose or
            // xsl:accept: neither can reach one, and one of the tests is written precisely to check that
            // naming it as a variable does not work. Listed all the same, marked, so that a reference to it
            // from another package is answered.
            m_components.Add(Component("variable", expanded, -1, element, isParameter: isParameter));
        }

        /// <summary>
        /// Gives an overriding global the slot of the global it overrides, and the overridden one a fresh
        /// slot of its own.
        /// </summary>
        /// <remarks>
        /// An override replaces the component for everyone, the used package included: a pattern or a body
        /// in the used package that read the original has to read the override from now on. Those were
        /// compiled against the original's slot when the used package was read, before this declaration
        /// existed, so the override takes that slot over and the original moves — where it stays reachable,
        /// as <c>$xsl:original</c>, from the override's own value and nowhere else.
        /// </remarks>
        private void TakeOverSlot(GlobalVariable overriding, ModuleElement source)
        {
            if (!m_overrideOf.TryGetValue(source, out ModuleElement usePackage)
                || !m_usePackageIds.TryGetValue(usePackage, out int used))
            {
                return;
            }

            // The used package's own declaration, or failing that the nearest one from any other package —
            // the used package may hold the name having accepted it from further down.
            int found = -1;

            for (int i = m_globals.Count - 2; i >= 0 && found < 0; i--)
            {
                if (m_globals[i].Name.Equals(overriding.Name) && PackageOf(m_globalElements[i].Tree) == used)
                {
                    found = i;
                }
            }

            for (int i = m_globals.Count - 2; i >= 0 && found < 0; i--)
            {
                if (m_globals[i].Name.Equals(overriding.Name) && PackageOf(m_globalElements[i].Tree) != m_package)
                {
                    found = i;
                }
            }

            if (found < 0)
            {
                return;
            }

            GlobalVariable original = m_globals[found];
            int taken = original.Slot;
            int moved = overriding.Slot;

            m_globals[found] = new GlobalVariable(
                original.Name, moved, null, null, original.IsParameter, original.Required, original.Type)
            {
                IsAbstract = original.IsAbstract,
            };
            m_globals[^1] = new GlobalVariable(
                overriding.Name, taken, null, null, overriding.IsParameter, overriding.Required, overriding.Type)
            {
                IsAbstract = overriding.IsAbstract,
            };

            for (int i = 0; i < m_scope.Count; i++)
            {
                if (m_scope[i].IsGlobal && m_scope[i].Slot == taken)
                {
                    m_scope[i] = new VariableBinding(m_scope[i].Name, moved, true);
                }
                else if (m_scope[i].IsGlobal && m_scope[i].Slot == moved)
                {
                    m_scope[i] = new VariableBinding(m_scope[i].Name, taken, true);
                }
            }

            m_originalSlots[taken] = moved;
        }

        /// <summary>
        /// Settles a <c>static</c> variable or parameter, there and then.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XSLT 3.0 §3.5. A static variable is not a global with a slot: it is a value the rest of the
        /// stylesheet is <em>compiled against</em>, which is what lets a <c>use-when</c> ask about one and
        /// what makes a reference to one a constant everywhere else. So it is evaluated as its declaration
        /// is read, before anything below it in the module has been looked at.
        /// </para>
        /// <para>
        /// Which also settles the scoping rule without a rule being written: what it may name is whatever is
        /// already in the table, and the table holds exactly the static variables declared before it.
        /// </para>
        /// </remarks>
        /// <param name="element">The declaring element.</param>
        /// <param name="expanded">Its resolved name.</param>
        /// <param name="name">The name as written, for messages.</param>
        /// <param name="isParameter">Whether it is an <c>xsl:param</c> rather than an <c>xsl:variable</c>.</param>
        private void DeclareStatic(int element, ExpandedName expanded, string name, bool isParameter)
        {
            if (HasContent(element) && GetAttribute(element, "select") is not null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0620,
                    $"The static {QualifiedNameOf(element)} '{name}' has both a select attribute and content. "
                    + "The value comes from one or the other.");
            }

            if (HasContent(element))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0010,
                    $"The static {QualifiedNameOf(element)} '{name}' has content. A static variable is "
                    + "settled before the stylesheet around it has been read, so there is no result tree to "
                    + "build one from; write a select expression instead.");
            }

            // Private to its package by nature, so it takes no visibility: the attribute is not one the
            // element has, whatever it says.
            if (GetAttribute(element, "visibility") is not null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0090,
                    $"The static {QualifiedNameOf(element)} '{name}' has a visibility attribute. A static "
                    + "variable is settled while the stylesheet is compiled and is nothing another package "
                    + "can see, so it has no visibility to declare.");
            }

            XdmSequenceType? declared = ReadDeclaredType(element);
            string? select = GetAttribute(element, "select");
            bool required = isParameter && ReadDeclarationFlag(element, "required");
            ExpandedName key = ScopedIn(m_package, expanded);

            if (required && select is not null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0010,
                    $"The static parameter '{name}' is declared required=\"yes\" and also given a default. A "
                    + "required parameter is one the caller must supply, so a default could never be used.");
            }

            // Two declarations of one name: the same module twice is a duplicate; one in a module still
            // being walked — the principal, or an ancestor of this one — outranks this one and stands; one
            // in a module already finished is outranked by this one, and, having been declared earlier in
            // tree order, has to agree with it (§9.7, XTSE3450).
            bool mustAgree = false;

            if (m_staticDeclarations.TryGetValue(key, out StaticDeclaration earlier))
            {
                // The same declaration again — a module imported twice — is already settled.
                if (ReferenceEquals(earlier.Tree, m_tree) && earlier.Element == element)
                {
                    return;
                }

                if (ReferenceEquals(earlier.Tree, m_tree))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0630,
                        $"'{name}' is declared twice as a static variable in one module.");
                }

                if (StaticRankOf(earlier.Tree) == int.MaxValue)
                {
                    return;
                }

                mustAgree = true;
            }

            XPathValue value;
            bool initialized = true;

            // A static parameter is supplied to the constructor rather than to the transformation, the whole
            // point of it being that the stylesheet is compiled differently depending on what it says.
            if (isParameter
                && m_options.Parameters is { } supplied
                && TryFindSuppliedParameter(supplied, expanded, out object? given))
            {
                value = XdmTypeConversion.Apply(
                    StylesheetParameters.Convert(name, given), declared, XsltErrorCode.XTTE0590);
                // The default is not analysed at all: a value supplied where the stylesheet is constructed
                // replaces it, and the suite's static-003a says in as many words that a forward reference
                // in the expression it replaced "is not an error anymore". misc/error's 0640o-2 asks for
                // the opposite on the same shape, and one of the two has to be wrong.
                initialized = false;
            }
            else if (required)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0050,
                    $"No value was supplied for the required static parameter '{name}'. A static parameter "
                    + "is supplied where the stylesheet is constructed, not where it is run, because what it "
                    + "says decides which parts of the stylesheet exist at all.");
            }
            else if (select is not null)
            {
                // Evaluated the way a use-when is, and for the same reason: there is no source document
                // here, so reading the context item is a mistake rather than a reading of the stylesheet. A
                // parameter whose default does not fit its type is implicitly mandatory, and nothing supplied
                // it.
                value = XdmTypeConversion.Apply(
                    EvaluateStatic(element, select),
                    declared,
                    isParameter ? XsltErrorCode.XTDE0050 : XsltErrorCode.XTTE0600);
            }
            else if (declared is null)
            {
                // No select, no content and no type: a zero-length string, as for any variable (§9.3).
                value = XPathValue.FromString(string.Empty);
            }
            else
            {
                // No select and a type: the empty sequence, if the type takes it. A parameter whose type
                // does not is mandatory, and nothing supplied it.
                value = XdmTypeConversion.Apply(
                    XPathValue.FromSequence(XdmSequence.Empty),
                    declared,
                    isParameter ? XsltErrorCode.XTDE0700 : XsltErrorCode.XTTE0600);
            }

            if (mustAgree && !Agrees(earlier, isParameter, initialized, m_staticValues[key], value))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE3450,
                    $"The static {QualifiedNameOf(element)} '{name}' is declared again here, at a higher import "
                    + "precedence than a declaration that came earlier in the stylesheet, and the two disagree. "
                    + "Whatever read the earlier one was given its value, so a later one may only say the same: "
                    + "the same kind of declaration, and the same value.");
            }

            m_staticValues[key] = value;
            m_staticDeclarations[key] = new StaticDeclaration(m_tree, element, isParameter, initialized);
        }

        private XPathValue EvaluateStatic(int element, string select)
        {
            int scope = m_scopeElement;
            m_scopeElement = element;
            bool wasInUseWhen = m_inUseWhen;
            m_inUseWhen = true;

            try
            {
                Expr expression = ParseExpression(element, select);
                // With the name table, so that a path over a tree the expression builds for itself — a
                // json-to-xml() and a step into it — reads names the way the runtime would.
                DynamicContext context = new DynamicContext(
                    m_tree, DynamicContext.NotANode, m_names.BuildFingerprintMap(m_tree), m_names);
                context.DocumentLoader = LoadStaticDocument;
                context.Collations = m_options.CollationResolver;

                return expression.Evaluate(ref context);
            }
            finally
            {
                m_inUseWhen = wasInUseWhen;
                m_scopeElement = scope;
            }
        }

        /// <summary>Whether two declarations of one static variable are consistent (§9.7).</summary>
        private static bool Agrees(
            StaticDeclaration earlier, bool isParameter, bool initialized, XPathValue was, XPathValue now)
        {
            if (earlier.IsParameter != isParameter)
            {
                return false;
            }

            if (!earlier.Initialized || !initialized)
            {
                return true;
            }

            try
            {
                return Xpath2FunctionExpr.DeepEqual(
                    XdmSequence.Items(was), XdmSequence.Items(now), Collation.Codepoint);
            }
            catch (XsltException)
            {
                // A function item has no identity to compare, and the rule says there may be none.
                return false;
            }
        }

        /// <summary>Finds what the caller supplied under a name, in either notation they may have written.</summary>
        private static bool TryFindSuppliedParameter(
            IReadOnlyDictionary<string, object?> supplied, ExpandedName name, out object? value)
        {
            foreach ((string key, object? given) in supplied)
            {
                if (StylesheetParameters.ParseName(key).Equals(name))
                {
                    value = given;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <inheritdoc/>
        public XPathValue? TryResolveStaticVariable(string namespaceUri, string localName)
        {
            // Keyed by package: a static variable is the package's own, and two packages may each have one
            // of the name.
            ExpandedName name = ScopedIn(CurrentPackage, new ExpandedName(namespaceUri, localName));

            if (!m_staticValues.TryGetValue(name, out XPathValue value))
            {
                return null;
            }

            // In a body, a non-static declaration of the name at a higher import precedence is the one a
            // reference means, the static one being shadowed as any global is; a static expression sees
            // only statics, there being nothing else settled yet.
            if (!m_inUseWhen
                && m_staticDeclarations.TryGetValue(name, out StaticDeclaration declared)
                && m_globalPrecedence.TryGetValue(name, out int precedence)
                && m_modulePrecedence.TryGetValue(declared.Tree, out int staticPrecedence)
                && precedence >= staticPrecedence)
            {
                return null;
            }

            return value;
        }

        /// <summary>The highest import precedence each non-static global is declared at.</summary>
        private readonly Dictionary<ExpandedName, int> m_globalPrecedence = new();

        /// <summary>The import precedence each module's declarations landed at.</summary>
        private readonly Dictionary<XdmTree, int> m_modulePrecedence = new();

        /// <summary>
        /// Reads an <c>xsl:namespace-alias</c>, which says that literal result elements written in one
        /// namespace should be produced in another.
        /// </summary>
        /// <remarks>
        /// It exists so a stylesheet can generate XSLT. A literal <c>&lt;xsl:template&gt;</c> would be read as
        /// an instruction rather than copied, so it is written under a stand-in namespace and aliased back to
        /// the real one on the way out.
        /// </remarks>
        private void ReadNamespaceAlias(int element, int precedence)
        {
            string stylesheetPrefix = GetAttribute(element, "stylesheet-prefix")
                ?? throw new XsltException("An xsl:namespace-alias must have a stylesheet-prefix.");

            string resultPrefix = GetAttribute(element, "result-prefix")
                ?? throw new XsltException("An xsl:namespace-alias must have a result-prefix.");

            string stylesheetUri = ResolveAliasPrefix(element, stylesheetPrefix);
            string resultUri = ResolveAliasPrefix(element, resultPrefix);
            string outputPrefix = resultPrefix == "#default" ? string.Empty : resultPrefix;

            // A declaration in an importing module outranks the one it imported.
            // Aliases are local to a package (§3.5.3), so the key carries the package.
            if (m_namespaceAliases.TryGetValue(ScopedUri(stylesheetUri), out NamespaceAlias existing)
                && existing.Precedence > precedence)
            {
                return;
            }

            // Two aliases of one literal namespace at one precedence, sending it two different places: the
            // stylesheet has not said which, and taking the later would make the result depend on the order
            // the modules were read in. A higher-precedence declaration settles it and was returned above.
            if (existing.Uri is not null && existing.Precedence == precedence && existing.Uri != resultUri)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0810,
                    $"Two xsl:namespace-alias declarations of the same import precedence send "
                    + $"'{stylesheetUri}' to both '{existing.Uri}' and '{resultUri}'. Nothing decides "
                    + "between them.");
            }

            m_namespaceAliases[ScopedUri(stylesheetUri)] = new NamespaceAlias(resultUri, outputPrefix, precedence);
        }

        private string ResolveAliasPrefix(int element, string prefix)
        {
            if (prefix == "#default")
            {
                return m_tree.ResolvePrefix(element, string.Empty) ?? string.Empty;
            }

            return m_tree.ResolvePrefix(element, prefix)
                ?? throw new XsltException(
                    $"Namespace prefix '{prefix}' on xsl:namespace-alias is not bound.");
        }

        /// <summary>
        /// Rewrites a name into its aliased namespace, if one was declared for it.
        /// </summary>
        /// <param name="prefix">The prefix as written in the stylesheet.</param>
        /// <param name="namespaceUri">The namespace URI as written in the stylesheet.</param>
        private (string Prefix, string Uri) ApplyNamespaceAlias(string prefix, string namespaceUri)
        {
            if (m_namespaceAliases.Count == 0)
            {
                return (prefix, namespaceUri);
            }

            return m_namespaceAliases.TryGetValue(ScopedUri(namespaceUri), out NamespaceAlias alias)
                ? (alias.Prefix, alias.Uri)
                : (prefix, namespaceUri);
        }

        /// <summary>Whether a namespace is the literal namespace of an alias in the package being compiled.</summary>
        private bool IsAliasSource(string uri)
        {
            return m_namespaceAliases.ContainsKey(ScopedUri(uri));
        }

        /// <summary>Whether a namespace is the target namespace of an alias in the package being compiled.</summary>
        private bool IsAliasTarget(string uri)
        {
            string scope = ScopedUri(string.Empty);

            foreach ((string source, NamespaceAlias alias) in m_namespaceAliases)
            {
                bool inPackage = scope.Length == 0
                    ? source.Length == 0 || source[0] != ScopeMark
                    : source.StartsWith(scope, StringComparison.Ordinal);

                if (inPackage && alias.Uri == uri)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly record struct NamespaceAlias(string Uri, string Prefix, int Precedence);

        private void DeclareAttributeSet(int element, ModuleElement source, int precedence)
        {
            string name = GetAttribute(element, "name")
                ?? throw new XsltException("An xsl:attribute-set must have a name.");

            ExpandedName expanded = ResolveQualifiedName(element, name);

            // An attribute set is held under its name marked with its package, so that a private one of a
            // used package and one of the same name here are two sets. The names it uses are resolved once
            // every declaration is known, in CompileAttributeSets: it may use one declared after it, or one
            // a used package offers.
            ExpandedName key = Scoped(expanded);

            if (!m_attributeSets.TryGetValue(key, out AttributeSet? set))
            {
                set = new AttributeSet(expanded);
                m_attributeSets.Add(key, set);
            }

            AttributeSetDeclaration declaration = new AttributeSetDeclaration(
                precedence, ReadAttributeSetNames(element, resolve: false))
            {
                IsAbstract = ReadVisibility(element) == Visibility.Abstract,
            };

            m_components.Add(Component("attribute-set", expanded, -1, element));

            set.Add(declaration);
            m_pendingAttributeSets.Add((declaration, source));

            if (IsOverriding(element))
            {
                ReplaceOverriddenAttributeSet(set, expanded, declaration, source);
            }
        }

        /// <summary>
        /// Puts an overriding attribute set where the one it overrides was, so that every use of the name
        /// — the used package's own included — finds the override, and keeps the original for xsl:original.
        /// </summary>
        private void ReplaceOverriddenAttributeSet(
            AttributeSet overriding, ExpandedName name, AttributeSetDeclaration declaration, ModuleElement source)
        {
            if (!m_overrideOf.TryGetValue(source, out ModuleElement usePackage)
                || !m_usePackageIds.TryGetValue(usePackage, out int used))
            {
                return;
            }

            PackageComponent? target = FindComponent("attribute-set", name, -1, used)
                ?? m_components.Find(component =>
                    component.Kind == "attribute-set"
                    && component.Name.Equals(name)
                    && component.Package != m_package
                    && Offers(used, component));

            if (target is null
                || !m_attributeSets.TryGetValue(ScopedIn(target.Package, name), out AttributeSet? original)
                || ReferenceEquals(original, overriding))
            {
                return;
            }

            // The original moves to a key of its own, in a spelling no name can have, and the override takes
            // the key every use of the used package's set resolves to.
            ExpandedName kept = new ExpandedName(
                ScopeMark + "original" + ScopeMark + m_attributeSets.Count.ToString(CultureInfo.InvariantCulture),
                name.LocalName);

            m_attributeSets[kept] = original;
            m_attributeSets[ScopedIn(target.Package, name)] = overriding;
            m_originalAttributeSets[declaration] = kept;
        }

        /// <summary>
        /// The key an attribute set is held under, for a use of its name from a package: the package's own
        /// declaration, or failing that the one a package it uses offers — either replaced by an override
        /// where there is one. Null where nothing this package can see answers to the name.
        /// </summary>
        private ExpandedName? AttributeSetKey(ExpandedName name, int package)
        {
            ExpandedName own = ScopedIn(package, name);

            if (m_attributeSets.ContainsKey(own))
            {
                return own;
            }

            foreach (PackageComponent component in m_components)
            {
                if (component.Kind == "attribute-set"
                    && component.Name.Equals(name)
                    && component.Package != package
                    && VisibleAs(component, package) is Visibility seen
                    && seen != Visibility.Hidden
                    && m_attributeSets.ContainsKey(ScopedIn(component.Package, name)))
                {
                    return ScopedIn(component.Package, name);
                }
            }

            return null;
        }

        /// <summary>Resolves the names an attribute set declaration uses, once every set is declared.</summary>
        private ExpandedName[] ResolvedUses(AttributeSetDeclaration declaration, int package)
        {
            ExpandedName[] resolved = new ExpandedName[declaration.Used.Length];

            for (int i = 0; i < resolved.Length; i++)
            {
                ExpandedName name = declaration.Used[i];

                if (name.Equals(XsltOriginal))
                {
                    resolved[i] = m_originalAttributeSets.TryGetValue(declaration, out ExpandedName kept)
                        ? kept
                        : throw XsltErrors.Error(
                            XsltErrorCode.XTSE0710,
                            "xsl:original names the attribute set this one replaced, and this attribute set "
                            + "is not inside an xsl:override.");
                    continue;
                }

                resolved[i] = AttributeSetKey(name, package) ?? name;
            }

            return resolved;
        }

        /// <summary>
        /// Reads an attribute holding a whitespace-separated list of names — <c>use-attribute-sets</c> and
        /// <c>use-character-maps</c> — into expanded names.
        /// </summary>
        /// <param name="element">The element carrying the attribute.</param>
        /// <param name="attributeName">The attribute to read, unprefixed or in the XSLT namespace.</param>
        private ExpandedName[] ReadQualifiedNameList(int element, string attributeName)
        {
            string? value = GetAttribute(element, attributeName) ?? GetXsltAttribute(element, attributeName);
            if (value is null)
            {
                return Array.Empty<ExpandedName>();
            }

            List<ExpandedName> names = new();
            foreach (string token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                names.Add(ResolveQualifiedName(element, token));
            }

            return names.ToArray();
        }

        /// <summary>
        /// Compiles the body of each attribute set, then checks that none of them draws in itself.
        /// </summary>
        private void CompileAttributeSets()
        {
            foreach ((AttributeSetDeclaration declaration, ModuleElement source) in m_pendingAttributeSets)
            {
                m_tree = source.Tree;
                m_scopeElement = source.Element;
                m_frameSlotCount = 0;
                declaration.Used = ResolvedUses(declaration, PackageOf(source.Tree));

                List<Instruction> attributes = new();

                for (int child = FirstIncludedChild(source.Element); child >= 0;
                    child = NextIncludedSibling(child))
                {
                    if (m_tree.KindOf(child) != NodeKind.Element)
                    {
                        continue;
                    }

                    if (!IsXsltElement(child, out string localName) || localName != "attribute")
                    {
                        throw new XsltException(
                            "An xsl:attribute-set may only contain xsl:attribute elements.");
                    }

                    CompileNode(child, attributes);
                }

                declaration.Attributes = attributes.ToArray();

                // After the body, since compiling it is what counts the slots.
                declaration.FrameSize = m_frameSlotCount;
            }

            foreach (AttributeSet set in m_attributeSets.Values)
            {
                CheckForAttributeSetCycle(set, new HashSet<AttributeSet>());
            }

        }

        /// <summary>
        /// Refuses a <c>use-attribute-sets</c> naming a set the stylesheet does not declare.
        /// </summary>
        /// <remarks>
        /// Collected as each use is read and checked at the end, because a set may be declared after the
        /// element that draws it in, in another module altogether, or inside a template that had not been
        /// compiled yet when the use was seen.
        /// </remarks>
        private void CheckAttributeSetsExist()
        {
            foreach ((ExpandedName name, string written, int package) in m_usedAttributeSets)
            {
                // Declared somewhere this package can see? One declared only by a used package that keeps
                // it private is, from here, not declared at all.
                if (AttributeSetKey(name, package) is null)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0710,
                        m_components.Exists(component => component.Kind == "attribute-set" && component.Name.Equals(name))
                            ? $"'{written}' is an attribute set of a package this one uses, and that package does "
                                + "not offer it. A component a package keeps private is one nothing outside the "
                                + "package can see, so from here it does not exist."
                            : $"'{written}' is used as an attribute set, and this stylesheet declares no "
                                + "xsl:attribute-set of that name.");
                }

                foreach (PackageComponent component in m_components)
                {
                    if (m_isPackage
                        && component.Kind == "attribute-set"
                        && component.Name.Equals(name)
                        && component.Effective == Visibility.Abstract
                        && !m_overridingKeys.Contains((package, "attribute-set", name, -1))
                        && VisibleAs(component, 0) == Visibility.Abstract)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3080,
                            $"'{written}' is abstract, and nothing has supplied it. A package meant to be "
                            + "run cannot refer to a component that only names what some package using it "
                            + "was to define.");
                    }
                }
            }
        }

        /// <summary>
        /// Reads a <c>use-attribute-sets</c>, remembering the names for the check that they exist.
        /// </summary>
        /// <param name="element">The element carrying the attribute.</param>
        /// <param name="resolve">
        /// Whether to resolve each name to the key of the set it means, which a use in a body can have done at
        /// once, every set being declared by then; a declaration's uses wait for CompileAttributeSets.
        /// </param>
        private ExpandedName[] ReadAttributeSetNames(int element, bool resolve = true)
        {
            string? value = GetAttribute(element, "use-attribute-sets")
                ?? GetXsltAttribute(element, "use-attribute-sets");

            if (value is null)
            {
                return Array.Empty<ExpandedName>();
            }

            List<ExpandedName> names = new();

            foreach (string token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IsLexicalQName(token))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0710,
                        $"'{token}' is not a name, so it cannot be an attribute set.");
                }

                ExpandedName name = ResolveQualifiedName(element, token);

                // xsl:original is a name only an overriding attribute set may use, for the set it replaced.
                if (name.Equals(XsltOriginal))
                {
                    if (resolve)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0710,
                            "xsl:original names the attribute set an override replaced, and only an "
                            + "xsl:attribute-set inside an xsl:override may use it.");
                    }

                    names.Add(name);
                    continue;
                }

                names.Add(resolve ? AttributeSetKey(name, CurrentPackage) ?? name : name);
                m_usedAttributeSets.Add((name, token, CurrentPackage));
            }

            return names.ToArray();
        }

        /// <summary>
        /// Walks the graph of <c>use-attribute-sets</c> references looking for a set that reaches itself,
        /// which would otherwise recurse without end at run time.
        /// </summary>
        private void CheckForAttributeSetCycle(AttributeSet set, HashSet<AttributeSet> visiting)
        {
            if (!visiting.Add(set))
            {
                throw new XsltException(
                    $"The attribute set '{set.Name.LocalName}' uses itself, directly or indirectly.");
            }

            foreach (AttributeSetDeclaration declaration in set.Declarations)
            {
                foreach (ExpandedName used in declaration.Used)
                {
                    if (m_attributeSets.TryGetValue(used, out AttributeSet? referenced))
                    {
                        CheckForAttributeSetCycle(referenced, visiting);
                    }
                }
            }

            visiting.Remove(set);
        }

        /// <summary>
        /// Checks that every <c>xsl:call-template</c> supplies the parameters the template it names requires.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reported while the stylesheet is compiled, because a call by name says exactly which template it
        /// reaches. Leaving it to run time would report the same mistake only when the call is executed, and
        /// a stylesheet whose error is on a branch nothing takes today is a stylesheet that fails tomorrow.
        /// </para>
        /// <para>
        /// Only ordinary parameters can be settled this way. A required tunnel parameter may be supplied by
        /// any invocation further out, so what reaches the template is not knowable from the call alone; that
        /// one waits for the invocation itself.
        /// </para>
        /// </remarks>
        private void CheckRequiredParameters()
        {
            foreach ((Template target, WithParameter[] supplied, string name, bool legacy) in m_pendingCalls)
            {
                foreach (TemplateParameter parameter in target.Parameters)
                {
                    if (!parameter.Required || parameter.Tunnel || IsSupplied(supplied, parameter.Name))
                    {
                        continue;
                    }

                    throw new XsltException(
                        $"The call to template '{name}' does not supply its required parameter "
                        + $"'{parameter.Name.LocalName}'.");
                }

                // The other direction, which XSLT 1.0 allowed and 2.0 does not: a parameter supplied to a
                // template that declares none of that name is discarded, and a stylesheet that writes one has
                // almost always misspelled it or is calling the template it meant to call by another name.
                if (legacy)
                {
                    continue;
                }

                foreach (WithParameter parameter in supplied)
                {
                    if (parameter.Tunnel || IsDeclared(target, parameter.Name))
                    {
                        continue;
                    }

                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0680,
                        $"The call to template '{name}' supplies a parameter "
                        + $"'{parameter.Name.LocalName}', and that template declares no such parameter. "
                        + "Nothing would read it.");
                }
            }
        }

        private static bool IsDeclared(Template target, ExpandedName name)
        {
            foreach (TemplateParameter parameter in target.Parameters)
            {
                if (!parameter.Tunnel && parameter.Name.Equals(name))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSupplied(WithParameter[] supplied, ExpandedName name)
        {
            foreach (WithParameter parameter in supplied)
            {
                if (!parameter.Tunnel && parameter.Name.Equals(name))
                {
                    return true;
                }
            }

            return false;
        }

        private readonly record struct CharacterMapDeclaration(ModuleElement Source, int Precedence);

        /// <summary>
        /// Records an <c>xsl:character-map</c>, without reading its content.
        /// </summary>
        /// <remarks>
        /// Only the name is settled here. What the map contains cannot be worked out yet, because
        /// <c>use-character-maps</c> may name one declared further down the stylesheet or in a module that is
        /// still to be loaded.
        /// </remarks>
        private void DeclareCharacterMap(int element, ModuleElement source, int precedence)
        {
            string name = GetAttribute(element, "name")
                ?? throw new XsltException("An xsl:character-map must have a name.");

            // Local to the package that declared it (§3.5.3), like the output definitions that use it.
            ExpandedName expanded = Scoped(ResolveQualifiedName(element, name));

            if (m_characterMaps.TryGetValue(expanded, out CharacterMapDeclaration existing))
            {
                if (existing.Precedence > precedence)
                {
                    // Already overridden by a module that imports this one.
                    return;
                }

                if (existing.Precedence == precedence)
                {
                    throw new XsltException(
                        $"Two character maps are named '{name}' with the same import precedence. One replaces "
                        + "the other only by being declared in a module that imports it.");
                }
            }

            m_characterMaps[expanded] = new CharacterMapDeclaration(source with { Element = element }, precedence);
        }

        /// <summary>
        /// Expands every declared character map into the substitutions it amounts to, and builds the map the
        /// principal result is written through.
        /// </summary>
        /// <remarks>
        /// Every map is expanded, not only the ones something names, so that a stylesheet is told about a
        /// character map that refers to itself or to one that does not exist even when nothing uses it.
        /// </remarks>
        private void ExpandCharacterMaps()
        {
            if (m_characterMaps.Count == 0 && m_outputCharacterMaps.Count == 0)
            {
                return;
            }

            foreach (ExpandedName name in m_characterMaps.Keys)
            {
                ExpandCharacterMap(name, new HashSet<ExpandedName>());
            }

            if (m_outputCharacterMaps.Count == 0)
            {
                return;
            }

            // Several xsl:output declarations may each name maps; they are merged in the order the modules
            // were read, so a declaration in an importing module has the last word — within one package,
            // each package's unnamed output definition being its own.
            Dictionary<int, Dictionary<int, string>> mergedByPackage = new();

            foreach (ModuleElement reference in m_outputCharacterMaps)
            {
                m_tree = reference.Tree;

                if (!mergedByPackage.TryGetValue(CurrentPackage, out Dictionary<int, string>? merged))
                {
                    mergedByPackage[CurrentPackage] = merged = new Dictionary<int, string>();
                }

                MergeCharacterMaps(reference.Element, merged);
            }

            foreach ((int package, Dictionary<int, string> merged) in mergedByPackage)
            {
                OutputSettingsFor(package).CharacterMap = new CharacterMap(merged);
            }
        }

        /// <summary>
        /// Expands one character map, drawing in the maps it names.
        /// </summary>
        /// <remarks>
        /// Maps named by <c>use-character-maps</c> are taken first and in the order written, and this map's
        /// own <c>xsl:output-character</c> children last. Where the same character is substituted more than
        /// once the last one wins, which is the specification's rule; that the children are the last is this
        /// engine's reading of it, and the one that makes a map override what it draws in.
        /// </remarks>
        private Dictionary<int, string> ExpandCharacterMap(ExpandedName name, HashSet<ExpandedName> visiting)
        {
            if (m_expandedCharacterMaps.TryGetValue(name, out Dictionary<int, string>? expanded))
            {
                return expanded;
            }

            if (!visiting.Add(name))
            {
                throw new XsltException(
                    $"The character map '{name.LocalName}' uses itself, directly or indirectly.");
            }

            CharacterMapDeclaration declaration = m_characterMaps[name];
            int element = declaration.Source.Element;
            Dictionary<int, string> entries = new Dictionary<int, string>();

            m_tree = declaration.Source.Tree;
            ExpandedName[] used = ReadQualifiedNameList(element, "use-character-maps");

            foreach (ExpandedName reference in used)
            {
                foreach (KeyValuePair<int, string> entry in RequireCharacterMap(Scoped(reference), visiting))
                {
                    entries[entry.Key] = entry.Value;
                }
            }

            // The recursion moved the compiler onto another module; this map's children are in its own.
            m_tree = declaration.Source.Tree;
            ReadOutputCharacters(element, entries);

            visiting.Remove(name);
            m_expandedCharacterMaps.Add(name, entries);
            return entries;
        }

        /// <summary>Merges the maps named by one <c>use-character-maps</c> attribute, later names winning.</summary>
        private void MergeCharacterMaps(int element, Dictionary<int, string> merged)
        {
            foreach (ExpandedName name in ReadQualifiedNameList(element, "use-character-maps"))
            {
                foreach (KeyValuePair<int, string> entry in RequireCharacterMap(Scoped(name), new HashSet<ExpandedName>()))
                {
                    merged[entry.Key] = entry.Value;
                }
            }
        }

        private Dictionary<int, string> RequireCharacterMap(ExpandedName name, HashSet<ExpandedName> visiting)
        {
            if (m_expandedCharacterMaps.TryGetValue(name, out Dictionary<int, string>? expanded))
            {
                return expanded;
            }

            if (!m_characterMaps.ContainsKey(name))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE1590,
                    $"No xsl:character-map is named '{name.LocalName}' in this package.");
            }

            return ExpandCharacterMap(name, visiting);
        }

        /// <summary>Reads the <c>xsl:output-character</c> children of a character map.</summary>
        private void ReadOutputCharacters(int element, Dictionary<int, string> entries)
        {
            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element)
                {
                    continue;
                }

                if (!IsXsltElement(child, out string localName) || localName != "output-character")
                {
                    throw new XsltException(
                        "An xsl:character-map may only contain xsl:output-character elements.");
                }

                string character = GetAttribute(child, "character")
                    ?? throw new XsltException("An xsl:output-character must have a 'character' attribute.");

                string replacement = GetAttribute(child, "string")
                    ?? throw new XsltException("An xsl:output-character must have a 'string' attribute.");

                entries[SingleCodePoint(character)] = replacement;
            }
        }

        /// <summary>
        /// Reads the one character an <c>xsl:output-character</c> substitutes.
        /// </summary>
        /// <remarks>
        /// Two <see cref="char"/> values are one character when they are a surrogate pair: a map may
        /// substitute a character outside the basic plane, and that is how such a character is written.
        /// </remarks>
        private static int SingleCodePoint(string character)
        {
            if (character.Length == 1 && !char.IsSurrogate(character[0]))
            {
                return character[0];
            }

            if (character.Length == 2
                && char.IsHighSurrogate(character[0])
                && char.IsLowSurrogate(character[1]))
            {
                return char.ConvertToUtf32(character[0], character[1]);
            }

            throw new XsltException(
                $"The 'character' attribute of an xsl:output-character is '{character}', which is not a "
                + "single character.");
        }

        private void DeclareKey(int element, ModuleElement source)
        {
            string name = GetAttribute(element, "name")
                ?? throw new XsltException("An xsl:key must have a name.");

            // The collation the key files strings under: the attribute, or failing that the default
            // collation in scope at the declaration (§20.2.1), which a default-collation on the xsl:key
            // itself or on the stylesheet puts there. Checked to exist now (XTSE1210) and kept as its URI;
            // the code point one, written or not, is no collation at all as far as the index cares.
            m_scopeElement = element;

            string? collation = (GetAttribute(element, "collation")?.Trim() ?? DefaultCollation) is string effective
                && effective is not ("" or Collation.CodepointUri)
                ? effective
                : null;

            if (collation is not null)
            {
                ResolveKnownCollation(collation, XsltErrorCode.XTSE1210);
            }

            ExpandedName expanded = ResolveQualifiedName(element, name);

            // Several declarations of one name in one package are one key (§20.2.1), filing what each of
            // them matches; a library's key of the same name is another key, its own package's.
            KeyDefinition? key = m_keys.Find(declared => declared.Name == expanded && declared.Package == CurrentPackage);

            if (key is null)
            {
                key = new KeyDefinition(expanded, m_keys.Count, CurrentPackage) { CollationUri = collation };
                m_keys.Add(key);
            }
            else if (!string.Equals(key.CollationUri, collation, StringComparison.Ordinal))
            {
                // One index, one way of filing: two declarations of a name filing strings under different
                // collations would be two keys answering to one name (XTSE1220).
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE1220,
                    $"The xsl:key declarations named '{name}' name different collations, and the "
                    + "declarations of one key file its values under one collation.");
            }

            key.Rules.Add(new KeyRule(source.Tree, source.Element));
        }

        /// <summary>
        /// Every NameTest an xsl:strip-space or xsl:preserve-space has named, with its precedence and the
        /// package it was named in — two packages saying different things about one element are not in
        /// conflict, each being answered in its own.
        /// </summary>
        private readonly Dictionary<(int Package, string? Uri, string Local), (int Precedence, bool Strip)>
            m_whitespaceDeclared = new();

        /// <summary>
        /// Declares one NameTest to the whitespace control, refusing the same test under both declarations
        /// at one precedence — a stylesheet saying two things about one element.
        /// </summary>
        private void DeclareWhitespace(
            int precedence, string? namespaceUri, string localName, bool strip)
        {
            (int, string?, string) test = (CurrentPackage, namespaceUri, localName);

            // XSLT 2.0 made this recoverable, and a 2.0 processor still gives the recovery: the later
            // declaration wins. From 3.0 it is a static error, and the suite has tests on either side —
            // the same stylesheet, run twice, and what decides is the processor and not what it says its
            // own version is.
            if (m_whitespaceDeclared.TryGetValue(test, out (int Precedence, bool Strip) earlier)
                && earlier.Precedence == precedence
                && earlier.Strip != strip
                && Implements30)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0270,
                    $"'{(namespaceUri is null ? "*:" : string.Empty)}{localName}' is named by both an "
                    + "xsl:strip-space and an xsl:preserve-space at the same import precedence, and nothing "
                    + "says which is meant.");
            }

            if (!m_whitespaceDeclared.ContainsKey(test) || earlier.Precedence < precedence)
            {
                m_whitespaceDeclared[test] = (precedence, strip);
            }

            WhitespaceOf(CurrentPackage).Declare(namespaceUri, localName, strip, precedence);
        }

        private void ReadWhitespaceControl(int element, bool strip, int precedence)
        {
            string elements = GetAttribute(element, "elements")
                ?? throw new XsltException(
                    $"An xsl:{(strip ? "strip" : "preserve")}-space must have an 'elements' attribute.");

            foreach (string token in elements.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "*")
                {
                    DeclareWhitespace(precedence, null, "*", strip);
                    continue;
                }

                // A name test written with the namespace in braces, which 3.0 admits wherever a name test
                // is written and needs no prefix to be in scope.
                if (token.StartsWith("Q{", StringComparison.Ordinal) && Implements30)
                {
                    int close = token.IndexOf('}');
                    if (close < 0)
                    {
                        throw new XsltException($"'{token}' opens with 'Q{{' and never closes it.");
                    }

                    DeclareWhitespace(precedence, token[2..close], token[(close + 1)..], strip);
                    continue;
                }

                int colon = token.IndexOf(':');
                if (colon < 0)
                {
                    // These are name tests and not names, so an unprefixed one is in the namespace
                    // xpath-default-namespace declares rather than in none.
                    DeclareWhitespace(precedence, DefaultElementNamespaceAt(element), token, strip);
                    continue;
                }

                string prefix = token[..colon];

                // '*:name' is the element of that local name in any namespace at all, which is a name test
                // like the others and not a prefix this stylesheet forgot to declare.
                if (prefix == "*")
                {
                    DeclareWhitespace(precedence, null, token[(colon + 1)..], strip);
                    continue;
                }

                string? uri = m_tree.ResolvePrefix(element, prefix);
                if (uri is null)
                {
                    throw new XsltException($"Namespace prefix '{prefix}' in '{token}' is not bound.");
                }

                DeclareWhitespace(precedence, uri, token[(colon + 1)..], strip);
            }
        }

        /// <summary>
        /// Reads the serialization attributes written on an <c>xsl:result-document</c>, over the top of what
        /// the stylesheet settled for the principal result.
        /// </summary>
        /// <remarks>
        /// Only the attributes actually written are changed, so a result document inherits everything it does
        /// not mention — which is what makes it reasonable to write one attribute rather than restate the
        /// whole of <c>xsl:output</c>.
        /// </remarks>
        private OutputSettings ReadLocalOutputSettings(int element, OutputSettings inherited)
        {
            OutputSettings settings = inherited.Copy();

            // Not the implicit result tree, so the rule keeping a 1.0 stylesheet away from the XHTML method
            // does not reach here: this instruction is one the stylesheet asked for by name.
            settings.MayInferXhtml = true;

            if (OutputAttribute(element, "method") is string method)
            {
                settings.Method = ReadOutputMethod(element, method);
                settings.MethodSpecified = true;
            }

            if (OutputAttribute(element, "indent") is string indent)
            {
                settings.Indent = IsYes(indent);
                settings.IndentSpecified = true;
            }

            if (OutputAttribute(element, "omit-xml-declaration") is string omit)
            {
                settings.OmitXmlDeclaration = IsYes(omit);
            }

            if (OutputAttribute(element, "encoding") is string encoding)
            {
                settings.Encoding = encoding;
            }

            if (OutputAttribute(element, "version") is string version)
            {
                settings.Version = version;
            }

            if (OutputAttribute(element, "standalone") is string standalone)
            {
                // Three values rather than two: "omit" leaves the declaration without a standalone at all,
                // which is not the same as saying it is not standalone.
                settings.Standalone = standalone.AsSpan().Trim() is "omit" ? null : IsYes(standalone);
            }

            if (OutputAttribute(element, "media-type") is string mediaType)
            {
                settings.MediaType = mediaType;
            }

            if (OutputAttribute(element, "doctype-public") is string publicId)
            {
                RequirePublicIdentifier(publicId);
                settings.DoctypePublic = publicId;
            }

            if (OutputAttribute(element, "doctype-system") is string systemId)
            {
                settings.DoctypeSystem = systemId;
            }

            ReadSerializationAddedIn30(element, settings);

            if (OutputAttribute(element, "byte-order-mark") is not null)
            {
                settings.ByteOrderMark = ReadDeclarationFlag(element, "byte-order-mark");
            }

            if (OutputAttribute(element, "include-content-type") is not null)
            {
                settings.IncludeContentType = ReadDeclarationFlag(element, "include-content-type");
            }

            if (OutputAttribute(element, "escape-uri-attributes") is not null)
            {
                settings.EscapeUriAttributes = ReadDeclarationFlag(element, "escape-uri-attributes");
            }

            if (OutputAttribute(element, "normalization-form") is string normalization)
            {
                settings.NormalizationForm = ReadNormalizationForm(normalization);
            }

            if (OutputAttribute(element, "use-character-maps") is not null)
            {
                // Naming maps here adds to what the output definition settled, the instruction's mappings
                // winning where the two map one character (§26.2): the one attribute that is a union.
                Dictionary<int, string> merged = settings.CharacterMap is CharacterMap existing
                    ? new Dictionary<int, string>(existing.Entries)
                    : new Dictionary<int, string>();
                MergeCharacterMaps(element, merged);
                settings.CharacterMap = new CharacterMap(merged);
            }

            ReadCDataSectionElements(element, settings);
            ReadParameterDocument(element, settings);

            return settings;
        }

        /// <summary>
        /// Builds the settings a named <c>xsl:output</c> declaration describes.
        /// </summary>
        /// <remarks>
        /// Read here rather than where the declaration stands, because a named output may use a character
        /// map declared further down the stylesheet. Each declaration is read against the module it was
        /// written in, so its prefixes mean what they did there.
        /// </remarks>
        /// <param name="element">The <c>xsl:result-document</c> asking, whose prefixes resolve the name.</param>
        /// <param name="format">The name as written.</param>
        private OutputSettings NamedOutputSettings(int element, string format)
        {
            ExpandedName name = ResolveQualifiedName(element, format);

            if (!m_namedOutputs.TryGetValue(Scoped(name), out List<ModuleElement>? declarations))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE1460,
                    $"'{format}' is not an output definition this stylesheet declares, and the format of an "
                    + "xsl:result-document has to name one.");
            }

            return NamedOutputSettingsOf(declarations);
        }

        /// <summary>What the declarations of one named output definition settle on, together.</summary>
        private OutputSettings NamedOutputSettingsOf(List<ModuleElement> declarations)
        {
            XdmTree asking = m_tree;
            int scope = m_scopeElement;
            OutputSettings settings = new OutputSettings();

            try
            {
                foreach (ModuleElement declaration in declarations)
                {
                    m_tree = declaration.Tree;
                    m_scopeElement = declaration.Element;
                    settings = ReadLocalOutputSettings(declaration.Element, settings);
                }
            }
            finally
            {
                m_tree = asking;
                m_scopeElement = scope;
            }

            return settings;
        }

        /// <summary>Every named output definition, for a format that is only known when the instruction runs.</summary>
        private Dictionary<ExpandedName, OutputSettings> AllNamedOutputSettings()
        {
            Dictionary<ExpandedName, OutputSettings> all = new();

            foreach ((ExpandedName scoped, List<ModuleElement> declarations) in m_namedOutputs)
            {
                if (InPackage(scoped, CurrentPackage, out ExpandedName name))
                {
                    all[name] = NamedOutputSettingsOf(declarations);
                }
            }

            return all;
        }

        /// <summary>
        /// Reads the method an <c>xsl:output</c> declares.
        /// </summary>
        /// <remarks>
        /// Written without a prefix it has to be one of the four the specification names. Written with one it
        /// names a method the processor may define for itself, and this one defines none — so that is refused
        /// for being unsupported rather than for being misspelt, which are different complaints and carry
        /// different codes.
        /// </remarks>
        /// <param name="element">The declaring element, whose prefixes resolve a prefixed name.</param>
        /// <param name="method">The value as written.</param>
        private OutputMethod ReadOutputMethod(int element, string method)
        {
            // A QName written in an attribute may be surrounded by whitespace, which is the XML rule for
            // every attribute of that type rather than anything this one says for itself.
            method = method.Trim();

            switch (method)
            {
                case "xml": return OutputMethod.Xml;
                case "html": return OutputMethod.Html;
                case "text": return OutputMethod.Text;
                case "xhtml": return OutputMethod.Xhtml;
                case "json" when Implements30: return OutputMethod.Json;
                case "adaptive" when Implements30: return OutputMethod.Adaptive;
            }

            int colon = method.IndexOf(':');
            string local = colon < 0 ? method : method[(colon + 1)..];

            if (colon <= 0 || local.Length == 0 || local.Contains(':')
                || m_tree.ResolvePrefix(element, method[..colon]) is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE1570,
                    $"'{method}' is not an output method. Without a prefix it has to be one of xml, html, "
                    + "xhtml and text; with one, it names a method the processor defines for itself.");
            }

            throw new XsltException($"Unsupported output method '{method}'.");
        }

        /// <summary>
        /// Refuses two <c>xsl:output</c> declarations of one output definition that disagree.
        /// </summary>
        /// <remarks>
        /// Several declarations of one definition are ordinary and useful — a module says what it needs and
        /// leaves the rest alone. Two at the same import precedence giving one attribute two different
        /// values are not: nothing decides between them, and taking the later would make the result depend
        /// on the order the declarations happened to be written in. A higher precedence overriding a lower
        /// one is the mechanism that <em>is</em> meant for saying it differently.
        /// <para>
        /// The two list-valued attributes are left out, since several declarations of those amount to their
        /// union rather than to a disagreement.
        /// </para>
        /// </remarks>
        /// <param name="element">The declaration.</param>
        /// <param name="precedence">The import precedence it was declared at.</param>
        private void RequireOutputAgrees(int element, int precedence)
        {
            ExpandedName definition = GetAttribute(element, "name") is string named
                ? ResolveQualifiedName(element, named)
                : new ExpandedName(string.Empty, string.Empty);

            int count = m_tree.AttributeCountOf(element);
            NameTable names = m_tree.NameTable;

            for (int i = 0; i < count; i++)
            {
                int attribute = m_tree.AttributeAt(element, i);
                int fingerprint = m_tree.FingerprintOf(attribute);

                if (names.GetNamespaceUri(fingerprint).Length != 0)
                {
                    continue;
                }

                string name = names.GetLocalName(fingerprint);

                if (name is "name" or "cdata-section-elements" or "use-character-maps")
                {
                    continue;
                }

                string value = m_tree.StringValueOf(attribute);

                if (m_outputAttributes.TryGetValue((definition, precedence, name), out string? earlier)
                    && earlier != value)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE1560,
                        $"Two xsl:output declarations of the same import precedence give '{name}' the "
                        + $"values '{earlier}' and '{value}'. Nothing decides between them.");
                }

                m_outputAttributes[(definition, precedence, name)] = value;
            }
        }

        private void ReadOutputSettings(int element, int precedence)
        {
            RequireOutputAgrees(element, precedence);

            if (GetAttribute(element, "name") is string named)
            {
                // A named output definition stands on its own. It neither starts from what the unnamed one
                // settled nor contributes to it, which is what lets one transformation write XML through the
                // unnamed definition and HTML through a named one.
                ExpandedName name = ResolveQualifiedName(element, named);

                // Only the settings are deferred, not the checking: a declaration nothing ever asks for is
                // still a declaration, and being unreachable does not make it well written.
                if (GetAttribute(element, "method") is string declared)
                {
                    ReadOutputMethod(element, declared);
                }

                if (!m_namedOutputs.TryGetValue(Scoped(name), out List<ModuleElement>? declarations))
                {
                    m_namedOutputs[Scoped(name)] = declarations = new List<ModuleElement>();
                }

                declarations.Add(new ModuleElement(m_tree, element));
                return;
            }

            // The unnamed output definition is local to a package (§3.5.3): a library's settles the result
            // documents its own instructions write without naming a format, and the principal result is the
            // top-level package's alone.
            OutputSettings settings = OutputSettingsFor(CurrentPackage);
            string? method = GetAttribute(element, "method");

            if (CurrentPackage == 0)
            {
                m_outputMethod = method is null ? m_outputMethod : ReadOutputMethod(element, method);
                settings.Method = m_outputMethod;
            }
            else if (method is not null)
            {
                settings.Method = ReadOutputMethod(element, method);
            }

            settings.MethodSpecified |= method is not null;

            if (GetAttribute(element, "indent") is string indent)
            {
                settings.Indent = IsYes(indent);
                settings.IndentSpecified = true;
            }

            if (GetAttribute(element, "omit-xml-declaration") is string omit)
            {
                settings.OmitXmlDeclaration = IsYes(omit);
            }

            if (GetAttribute(element, "encoding") is string encoding)
            {
                settings.Encoding = encoding;
            }

            if (GetAttribute(element, "version") is string version)
            {
                settings.Version = version;
            }

            if (GetAttribute(element, "standalone") is string standalone)
            {
                settings.Standalone = standalone.AsSpan().Trim() is "omit" ? null : IsYes(standalone);
            }

            if (GetAttribute(element, "media-type") is string mediaType)
            {
                settings.MediaType = mediaType;
            }

            if (GetAttribute(element, "doctype-public") is string publicId)
            {
                RequirePublicIdentifier(publicId);
                settings.DoctypePublic = publicId;
            }

            settings.DoctypeSystem = GetAttribute(element, "doctype-system") ?? settings.DoctypeSystem;

            if (GetAttribute(element, "byte-order-mark") is not null)
            {
                settings.ByteOrderMark = ReadDeclarationFlag(element, "byte-order-mark");
            }

            if (GetAttribute(element, "include-content-type") is not null)
            {
                settings.IncludeContentType = ReadDeclarationFlag(element, "include-content-type");
            }

            if (GetAttribute(element, "escape-uri-attributes") is not null)
            {
                settings.EscapeUriAttributes = ReadDeclarationFlag(element, "escape-uri-attributes");
            }

            if (GetAttribute(element, "normalization-form") is string normalization)
            {
                settings.NormalizationForm = ReadNormalizationForm(normalization);
            }

            if (GetAttribute(element, "use-character-maps") is not null)
            {
                // Kept until every module is read; the maps it names need not be declared yet.
                m_outputCharacterMaps.Add(new ModuleElement(m_tree, element));
            }

            ReadCDataSectionElements(element, settings);
            ReadSerializationAddedIn30(element, settings);
            ReadParameterDocument(element, settings);
        }

        /// <summary>
        /// Reads the <c>parameter-document</c> an <c>xsl:output</c> or <c>xsl:result-document</c> names,
        /// whose parameters take precedence over the attributes written alongside (§26.1).
        /// </summary>
        /// <remarks>
        /// The document is fetched through the stylesheet resolver, since it is read while the stylesheet is
        /// compiled and settles how the stylesheet's own results are written: it is part of the stylesheet's
        /// configuration rather than data it processes. One that cannot be found is ignored, which is what
        /// the specification says of it — the attribute is a way of keeping settings outside the stylesheet,
        /// not a requirement on the deployment.
        /// </remarks>
        private void ReadParameterDocument(int element, OutputSettings settings)
        {
            if (OutputAttribute(element, "parameter-document") is not string href
                || m_options.StylesheetResolver is null
                || m_options.StylesheetResolver.Resolve(href.Trim(), StaticBaseUri(element)) is not ResolvedResource resolved)
            {
                return;
            }

            XdmTree document;

            try
            {
                document = XdmTreeBuilder.FromXml(resolved.Reader, entityResolver: m_options.EntityResolver, baseUri: resolved.Uri);
            }
            finally
            {
                resolved.Reader.Dispose();
            }

            int root = -1;

            for (int child = document.FirstChildOf(XdmTree.RootNode); child >= 0; child = document.NextSiblingOf(child))
            {
                if (document.KindOf(child) == NodeKind.Element)
                {
                    root = child;
                    break;
                }
            }

            if (root < 0
                || NamespaceIn(document, root) != Serializer.ParameterNamespace
                || LocalNameIn(document, root) != "serialization-parameters")
            {
                throw XsltErrors.Error(
                    XsltErrorCode.SEPM0017,
                    $"The parameter-document '{href}' of {QualifiedNameOf(element)} does not hold an "
                    + "output:serialization-parameters element.");
            }

            for (int child = document.FirstChildOf(root); child >= 0; child = document.NextSiblingOf(child))
            {
                if (document.KindOf(child) != NodeKind.Element)
                {
                    continue;
                }

                string name = LocalNameIn(document, child);

                if (NamespaceIn(document, child) != Serializer.ParameterNamespace)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.SEPM0017,
                        $"The parameter-document '{href}' names '{name}' outside the serialization namespace.");
                }

                if (name == "use-character-maps")
                {
                    Dictionary<int, string> entries = new Dictionary<int, string>();

                    for (int map = document.FirstChildOf(child); map >= 0; map = document.NextSiblingOf(map))
                    {
                        if (document.KindOf(map) != NodeKind.Element || LocalNameIn(document, map) != "character-map")
                        {
                            continue;
                        }

                        string character = AttributeIn(document, map, "character") ?? string.Empty;
                        string replacement = AttributeIn(document, map, "map-string") ?? string.Empty;

                        if (character.Length == 0 || char.ConvertToUtf32(character, 0) is int codePoint
                            && character.Length != (codePoint > 0xFFFF ? 2 : 1))
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.SEPM0017,
                                $"A character-map in the parameter-document '{href}' maps '{character}', which "
                                + "is not one character.");
                        }

                        entries[char.ConvertToUtf32(character, 0)] = replacement;
                    }

                    settings.CharacterMap = new CharacterMap(entries);
                    continue;
                }

                string value = AttributeIn(document, child, "value")
                    ?? throw XsltErrors.Error(
                        XsltErrorCode.SEPM0017,
                        $"The parameter '{name}' in the parameter-document '{href}' has no value attribute.");

                ApplyDocumentParameter(document, child, href, name, value, settings);
            }
        }

        private void ApplyDocumentParameter(XdmTree document, int node, string href, string name, string value, OutputSettings settings)
        {
            bool Flag()
            {
                return value.Trim() switch
                {
                    "yes" or "true" or "1" => true,
                    "no" or "false" or "0" => false,
                    _ => throw XsltErrors.Error(
                        XsltErrorCode.SEPM0017,
                        $"The parameter '{name}' in the parameter-document '{href}' is '{value}', and it takes "
                        + "yes or no."),
                };
            }

            OutputMethod Method()
            {
                return value.Trim() switch
                {
                    "xml" => OutputMethod.Xml,
                    "html" => OutputMethod.Html,
                    "xhtml" => OutputMethod.Xhtml,
                    "text" => OutputMethod.Text,
                    "json" when Implements30 => OutputMethod.Json,
                    "adaptive" when Implements30 => OutputMethod.Adaptive,
                    _ => throw XsltErrors.Error(
                        XsltErrorCode.XTSE1570,
                        $"The parameter '{name}' in the parameter-document '{href}' is '{value}', which is not "
                        + "an output method."),
                };
            }

            List<(string NamespaceUri, string LocalName)> Names()
            {
                List<(string, string)> names = new();

                foreach (string token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = token.IndexOf(':');
                    string prefix = colon < 0 ? string.Empty : token[..colon];
                    string uri = colon < 0
                        ? string.Empty
                        : document.ResolvePrefix(node, prefix)
                            ?? throw XsltErrors.Error(
                                XsltErrorCode.SEPM0017,
                                $"The prefix of '{token}' in the parameter-document '{href}' is not declared.");

                    names.Add((uri, token[(colon + 1)..]));
                }

                return names;
            }

            switch (name)
            {
                case "method":
                    settings.Method = Method();
                    settings.MethodSpecified = true;

                    if (CurrentPackage == 0 && ReferenceEquals(settings, OutputSettingsFor(0)))
                    {
                        m_outputMethod = settings.Method;
                    }

                    break;

                case "json-node-output-method":
                    settings.JsonNodeOutputMethod = Method() is OutputMethod.Json or OutputMethod.Adaptive
                        ? throw XsltErrors.Error(
                            XsltErrorCode.SEPM0017,
                            $"The json-node-output-method in the parameter-document '{href}' is '{value}', and a "
                            + "node inside a JSON string is written with xml, html, xhtml or text.")
                        : Method();
                    break;

                case "indent":
                    settings.Indent = Flag();
                    settings.IndentSpecified = true;
                    break;

                case "omit-xml-declaration": settings.OmitXmlDeclaration = Flag(); break;
                case "byte-order-mark": settings.ByteOrderMark = Flag(); break;
                case "escape-uri-attributes": settings.EscapeUriAttributes = Flag(); break;
                case "include-content-type": settings.IncludeContentType = Flag(); break;
                case "allow-duplicate-names": settings.AllowDuplicateNames = Flag(); break;
                case "build-tree": settings.BuildTree = Flag(); break;
                case "undeclare-prefixes": break;
                case "standalone": settings.Standalone = value.Trim() == "omit" ? null : Flag(); break;
                case "encoding": settings.Encoding = value.Trim(); break;
                case "version": settings.Version = value.Trim(); break;
                case "media-type": settings.MediaType = value.Trim(); break;
                case "doctype-public": settings.DoctypePublic = value; break;
                case "doctype-system": settings.DoctypeSystem = value; break;
                case "item-separator": settings.ItemSeparator = value; break;
                case "normalization-form": settings.NormalizationForm = ReadNormalizationForm(value); break;

                case "html-version":
                    settings.HtmlVersion = decimal.TryParse(
                        value.Trim(),
                        System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out decimal parsed)
                        ? parsed
                        : throw XsltErrors.Error(
                            XsltErrorCode.SEPM0017,
                            $"The html-version in the parameter-document '{href}' is '{value}', which is not a number.");
                    break;

                case "cdata-section-elements":
                    settings.CDataSectionElements.AddRange(Names());
                    break;

                case "suppress-indentation":
                    settings.SuppressIndentation.AddRange(Names());
                    break;

                default:
                    throw XsltErrors.Error(
                        XsltErrorCode.SEPM0017,
                        $"'{name}' in the parameter-document '{href}' is not a serialization parameter.");
            }
        }

        private static string LocalNameIn(XdmTree tree, int node) => tree.NameTable.GetLocalName(tree.FingerprintOf(node));

        private static string NamespaceIn(XdmTree tree, int node) => tree.NameTable.GetNamespaceUri(tree.FingerprintOf(node));

        private static string? AttributeIn(XdmTree tree, int element, string name)
        {
            for (int i = 0; i < tree.AttributeCountOf(element); i++)
            {
                int attribute = tree.AttributeAt(element, i);

                if (LocalNameIn(tree, attribute) == name && NamespaceIn(tree, attribute).Length == 0)
                {
                    return tree.StringValueOf(attribute);
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the serialization attributes XSLT 3.0 added, for either <c>xsl:output</c> or
        /// <c>xsl:result-document</c>.
        /// </summary>
        /// <remarks>
        /// One method for both because the two readers are otherwise parallel by hand, and three attributes
        /// added to one of them and not the other is exactly the kind of divergence that shows up as a
        /// feature that works on the principal result and not on a second one.
        /// </remarks>
        private void ReadSerializationAddedIn30(int element, OutputSettings settings)
        {
            if (OutputAttribute(element, "html-version") is string htmlVersion)
            {
                settings.HtmlVersion = decimal.TryParse(
                    htmlVersion.Trim(),
                    System.Globalization.NumberStyles.AllowLeadingSign
                        | System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal parsed)
                    ? parsed
                    : throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"The html-version of {QualifiedNameOf(element)} is '{htmlVersion}', which is not a "
                        + "number. It names a version of HTML, so '5' and '5.0' say the same thing.");
            }

            // "#absent" is how a result document says it wants no separator specified rather than an empty
            // one, since an attribute cannot be written to be absent.
            if (OutputAttribute(element, "item-separator") is string separator)
            {
                settings.ItemSeparator = separator == "#absent" ? null : separator;
            }

            if (OutputAttribute(element, "build-tree") is not null)
            {
                settings.BuildTree = ReadDeclarationFlag(element, "build-tree");
            }

            if (OutputAttribute(element, "allow-duplicate-names") is not null)
            {
                settings.AllowDuplicateNames = ReadDeclarationFlag(element, "allow-duplicate-names");
            }

            if (OutputAttribute(element, "json-node-output-method") is string nodeMethod)
            {
                OutputMethod read = ReadOutputMethod(element, nodeMethod);

                settings.JsonNodeOutputMethod = read is OutputMethod.Json or OutputMethod.Adaptive
                    ? throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"The json-node-output-method of {QualifiedNameOf(element)} is '{nodeMethod}', and a "
                        + "node inside a JSON string is written with xml, html, xhtml or text.")
                    : read;
            }

            ReadSuppressIndentation(element, settings);
        }

        /// <summary>
        /// Second pass: compile the value of each global and the body of each template, now that every name is
        /// known.
        /// </summary>
        private void CompileTemplateBodies()
        {
            for (int i = 0; i < m_globals.Count; i++)
            {
                GlobalVariable declared = m_globals[i];
                ModuleElement source = m_globalElements[i];

                // Each declaration is compiled against the module it was written in, so its prefixes and
                // relative paths mean what they did there.
                m_tree = source.Tree;
                m_scopeElement = source.Element;
                m_frameSlotCount = 0;

                m_compilingGlobalSlot = declared.Slot;
                (Expr? select, Instruction[]? body) = CompileVariableValue(source.Element);
                m_compilingGlobalSlot = -1;

                // A library's globals have no context item (§2.3.2): the global context item is the
                // top-level package's, and a library is written without knowing what it will be run on.
                m_globals[i] = new GlobalVariable(
                    declared.Name,
                    declared.Slot,
                    select,
                    body,
                    declared.IsParameter,
                    declared.Required,
                    declared.Type)
                {
                    // Recorded after the body is compiled, since compiling it is what counts the slots.
                    FrameSize = m_frameSlotCount,
                    IsAbstract = declared.IsAbstract,
                    InLibrary = PackageOf(source.Tree) != 0,
                    BaseUri = StaticBaseUri(source.Element),
                };
            }

            foreach ((Template template, ModuleElement source) in m_pendingTemplates)
            {
                m_tree = source.Tree;

                if (source.Simplified)
                {
                    CompileSimplifiedTemplate(template, source.Element);
                    continue;
                }

                CompileTemplate(template, source.Element);
            }
        }

        /// <summary>
        /// Compiles a simplified stylesheet's implicit template, whose body is the document element itself
        /// rather than that element's children.
        /// </summary>
        private void CompileSimplifiedTemplate(Template template, int element)
        {
            m_frameSlotCount = 0;
            m_scopeElement = element;

            template.Body = new[] { CompileLiteralElement(element) };
            template.FrameSize = m_frameSlotCount;
        }

        private void CompileTemplate(Template template, int element)
        {
            m_compilingTemplate = template;
            m_frameSlotCount = 0;
            int scopeMark = m_scope.Count;

            List<TemplateParameter> parameters = new List<TemplateParameter>();
            List<Instruction> body = new List<Instruction>();

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element && IsXsltElement(child, out string localName))
                {
                    if (localName == "param")
                    {
                        parameters.Add(CompileTemplateParameter(child));
                        continue;
                    }

                    if (localName == "context-item" && Implements30)
                    {
                        m_scopeElement = child;
                        template.ContextItem = ReadContextItem(child);

                        // A template rule is reached by matching a node, so there is always an item there,
                        // and saying it is absent is not a promise the rule is in a position to make. Unless
                        // it also has a name: then there is a way in that supplies no item, the declaration
                        // is about that way in, and reaching it as a rule is a dynamic error instead.
                        if (template.ContextItem.Use == ContextItemUse.Absent
                            && template.Patterns.Length > 0
                            && template.Name is null)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE0020,
                                "This template has a match pattern, so it is reached by matching a node — "
                                + "and cannot declare that there is no context item.");
                        }

                        continue;
                    }
                }

            }

            body.AddRange(CompileSequence(element, name => name is "param" or "context-item"));

            template.Parameters = parameters.ToArray();
            template.Body = body.ToArray();
            template.FrameSize = m_frameSlotCount;

            // An xsl:call-template that is the last thing the template does — itself, or through the
            // branches of an xsl:if or xsl:choose — is made in place of this invocation rather than beneath
            // it, so a template that recurses that way is a loop rather than a stack.
            Instruction.MarkTailPosition(template.Body);

            m_scope.RemoveRange(scopeMark, m_scope.Count - scopeMark);
        }

        /// <summary>
        /// Whether an element exists in the version of XSLT in scope where it was written.
        /// </summary>
        /// <remarks>
        /// A 3.0 element written in a <c>version="2.0"</c> stylesheet is not an element at all, whatever
        /// this engine happens to have implemented. That matters beyond pedantry: a 2.0 stylesheet that
        /// wrote an <c>xsl:fallback</c> inside an <c>xsl:try</c> did so precisely because 2.0 has no
        /// <c>xsl:try</c>, and taking the instruction instead of the fallback changes what it means.
        /// </remarks>
        /// <summary>
        /// Reads a component's <c>visibility</c>.
        /// </summary>
        /// <remarks>
        /// A stylesheet that is not a package has no boundary for visibility to be about, so its components
        /// are public: there is nothing outside to hide them from, and treating them as private would make
        /// every named template of every ordinary stylesheet ineligible as an entry point.
        /// </remarks>
        private Visibility ReadVisibility(int element)
        {
            return DeclaredVisibility(element) ?? (m_isPackage ? Visibility.Private : Visibility.Public);
        }

        /// <summary>
        /// Reads a component's <c>visibility</c>, distinguishing "said nothing" from "said the default".
        /// </summary>
        /// <remarks>
        /// The difference is what <c>xsl:expose</c> turns on. A declaration that stated its own visibility
        /// has settled the question and an xsl:expose disagreeing with it is an error; one that stated
        /// nothing is waiting to be told. Folding the default in here would make the two indistinguishable
        /// by the time anything could ask.
        /// </remarks>
        /// <param name="element">The declaration.</param>
        /// <returns>What the attribute said, or null where there was none.</returns>
        private Visibility? DeclaredVisibility(int element)
        {
            string? said = GetAttribute(element, "visibility")?.Trim();

            if (said is null)
            {
                return null;
            }

            return said switch
            {
                "public" => Visibility.Public,
                "private" => Visibility.Private,
                "final" => Visibility.Final,
                "abstract" => Visibility.Abstract,
                "hidden" => Visibility.Hidden,
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{said}' is not one of the values 'visibility' may take: public, private, final, "
                    + "abstract, hidden."),
            };
        }

        // ---- xsl:expose ----------------------------------------------------------------------------------

        /// <summary>One component of a package, as <c>xsl:expose</c> sees it.</summary>
        /// <remarks>
        /// A component is a named thing one package can offer another: a named template, a function of a
        /// given arity, an attribute set, a global variable, a mode. The declarations have almost nothing
        /// else in common, so this is the whole of what <c>xsl:expose</c> is given of each — a name to match
        /// against, whether the declaration stated a visibility of its own, and somewhere to put the answer.
        /// </remarks>
        private sealed class PackageComponent
        {
            /// <summary>Initializes a component.</summary>
            /// <param name="kind">What sort of component it is, spelled as <c>xsl:expose</c> spells it.</param>
            /// <param name="name">The component's name.</param>
            /// <param name="arity">How many parameters, for a function; -1 for a kind with no arity.</param>
            /// <param name="declared">What the declaration's own <c>visibility</c> said, or null.</param>
            /// <param name="apply">Records a visibility, where the component can hold one.</param>
            /// <param name="package">The package the declaration was written in.</param>
            public PackageComponent(
                string kind,
                ExpandedName name,
                int arity,
                Visibility? declared,
                Action<Visibility>? apply,
                int package)
            {
                Kind = kind;
                Name = name;
                Arity = arity;
                Declared = declared;
                Apply = apply;
                Package = package;
            }

            /// <summary>The package the declaration was written in.</summary>
            public int Package { get; }

            /// <summary>
            /// What the component's own package ends up offering it as.
            /// </summary>
            /// <remarks>
            /// The declaration's own <c>visibility</c> where it stated one, otherwise whatever an
            /// <c>xsl:expose</c> in that package settled on, otherwise private. This is what an
            /// <c>xsl:accept</c> in another package is entitled to ask for no more than.
            /// </remarks>
            public Visibility Effective { get; set; } = Visibility.Private;

            /// <summary>Whether an <c>xsl:expose</c> settled the visibility, as against the default standing.</summary>
            public bool Exposed { get; set; }

            /// <summary>What sort of component it is, spelled as <c>xsl:expose</c> spells it.</summary>
            public string Kind { get; }

            /// <summary>The component's name.</summary>
            public ExpandedName Name { get; }

            /// <summary>How many parameters, for a function; -1 for a kind that has no arity.</summary>
            public int Arity { get; }

            /// <summary>Whether the declaration stood in an <c>xsl:override</c>, replacing a used package's.</summary>
            public bool IsOverride { get; init; }

            /// <summary>
            /// The function this component was declared by, where it is a function.
            /// </summary>
            /// <remarks>
            /// Kept because the table of functions is keyed by name and arity alone and holds one entry for
            /// each: where a package overrides another's function, that entry is the override, and the
            /// declaration it replaced is reachable only from the component that declared it. Which is what
            /// a <c>function-lookup()</c> written inside the used package has to find.
            /// </remarks>
            public UserFunction? Function { get; set; }

            /// <summary>
            /// Whether the declaration is an <c>xsl:param</c>, which is a variable to whoever refers to it
            /// and not a component to <c>xsl:expose</c> or <c>xsl:accept</c>.
            /// </summary>
            public bool IsParameter { get; init; }

            /// <summary>The declaration, where the component has one an override's signature is checked against.</summary>
            public ModuleElement? Element { get; init; }

            /// <summary>What the declaration's own <c>visibility</c> said, or null where it said nothing.</summary>
            public Visibility? Declared { get; }

            /// <summary>Records a visibility an <c>xsl:expose</c> settled on.</summary>
            public Action<Visibility>? Apply { get; }
        }

        /// <summary>One name written in an <c>xsl:expose</c>, which may be a wildcard in either half.</summary>
        /// <param name="NamespaceUri">The namespace to match, or null for any.</param>
        /// <param name="LocalName">The local name to match, or null for any.</param>
        /// <param name="Arity">The arity written after a <c>#</c>, or -1 where none was.</param>
        private readonly record struct ExposedName(string? NamespaceUri, string? LocalName, int Arity)
        {
            /// <summary>Whether either half was written as <c>*</c>.</summary>
            public bool IsWildcard => NamespaceUri is null || LocalName is null;

            /// <summary>
            /// How particular the name is, which is what settles it where a component matches two of them.
            /// </summary>
            public int Specificity => (NamespaceUri is null ? 0 : 1) + (LocalName is null ? 0 : 1);
        }

        /// <summary>One <c>xsl:expose</c> declaration, read.</summary>
        private sealed record class Exposure
        {
            /// <summary>The kind of component named, or <c>*</c> for every kind.</summary>
            public string Component { get; init; } = "*";

            /// <summary>The visibility the components named are to be given.</summary>
            public Visibility Visibility { get; init; }

            /// <summary>The names written, in the order written.</summary>
            public ExposedName[] Names { get; init; } = Array.Empty<ExposedName>();

            /// <summary>The package it was written in, which is the only one it is about.</summary>
            public int Package { get; init; }
        }

        /// <summary>
        /// Reads every <c>xsl:expose</c> and settles what each component of the package offers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In three passes, and the order of them is not arbitrary. The specification defines four errors
        /// here, and the test suite pins down which one a stylesheet with more than one fault is entitled to
        /// hear about: the syntax of the attributes first (<c>XTSE0020</c>, <c>XTSE3022</c>), then the
        /// visibilities of what was matched (<c>XTSE3010</c>, <c>XTSE3025</c>), and only then the names that
        /// matched nothing (<c>XTSE3020</c>).
        /// </para>
        /// <para>
        /// The last two being that way round is the surprising part, and it is what erratum E36 costs. E36
        /// made an arity compulsory when naming a function, which turned a line written in half the tests in
        /// this area into a name matching nothing — so checking names before visibilities would answer nine
        /// of them with <c>XTSE3020</c> where the fault they were written to show is a visibility.
        /// </para>
        /// </remarks>
        private void ApplyExposures()
        {
            List<Exposure> exposures = new(m_exposures.Count);

            foreach ((ModuleElement source, int package) in m_exposures)
            {
                m_tree = source.Tree;
                exposures.Add(ReadExposure(source.Element) with { Package = package });
            }

            foreach (Exposure exposure in exposures)
            {
                foreach (PackageComponent component in m_components)
                {
                    if (BestMatch(exposure, component) is int specificity)
                    {
                        CheckVisibilityChange(exposure, component, exact: specificity == 2);
                    }
                }
            }

            foreach (Exposure exposure in exposures)
            {
                foreach (ExposedName name in exposure.Names)
                {
                    if (name.IsWildcard
                        || m_components.Exists(component => Matches(exposure, name, component)))
                    {
                        continue;
                    }

                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3020,
                        $"xsl:expose names {Written(name)} as {Article(exposure.Component)} of this "
                        + "package, and the package declares no such component.");
                }
            }

            // Applied only once every declaration has been checked, so that a stylesheet that is going to be
            // refused is not half-rewritten first.
            foreach (PackageComponent component in m_components)
            {
                Exposure? winner = null;
                int best = -1;

                foreach (Exposure exposure in exposures)
                {
                    if (BestMatch(exposure, component) is int specificity
                        && Rank(exposure, specificity) >= best)
                    {
                        best = Rank(exposure, specificity);
                        winner = exposure;
                    }
                }

                // A declaration that stated its own visibility has settled the question, so there is nothing
                // for an xsl:expose to add: what reaches the second branch is only what said nothing. The
                // loop runs even where the package wrote no xsl:expose at all, because settling the
                // effective visibility of every component is the other half of what it is for, and skipping
                // it left every component of an exposeless package looking private to the xsl:accept that
                // named it.
                if (component.Declared is Visibility stated)
                {
                    component.Effective = stated;
                }
                else if (winner is not null)
                {
                    component.Effective = winner.Visibility;
                    component.Exposed = true;
                    component.Apply?.Invoke(winner.Visibility);
                }
            }
        }


        /// <summary>One <c>xsl:accept</c>, and the package it is about.</summary>
        /// <param name="Source">The declaration.</param>
        /// <param name="Package">The package the enclosing <c>xsl:use-package</c> brought in.</param>
        /// <param name="Using">The package doing the using, whose account of the component this is.</param>
        private readonly record struct Acceptance(ModuleElement Source, int Package, int Using);

        /// <summary>
        /// Checks every <c>xsl:accept</c> against what the package it names actually offers.
        /// </summary>
        /// <remarks>
        /// The mirror of <see cref="ApplyExposures"/> and deliberately built on the same pieces: an
        /// xsl:accept says the same sort of thing from the other side of the boundary, so it reads the same
        /// attributes with the same name tests and asks the same questions of the same component list. What
        /// differs is which package the components come from — the one used rather than the one using — and
        /// that visibility here may only be reduced, an <c>xsl:accept</c> being a using package's account of
        /// what it wants and never a way to help itself to more than it was offered.
        /// </remarks>
        private void CheckAcceptances()
        {
            foreach (Acceptance acceptance in m_acceptances)
            {
                m_tree = acceptance.Source.Tree;
                Exposure accept = ReadExposure(acceptance.Source.Element, accepting: true)
                    with { Package = acceptance.Package };

                foreach (PackageComponent component in m_components)
                {
                    if (BestMatch(accept, component, offeredToo: true) is not int specificity)
                    {
                        continue;
                    }

                    // What the used package holds the component as: its own declaration's visibility, or
                    // for one it accepted from further down, whatever it holds that as.
                    Visibility offered = component.Package == accept.Package
                        ? component.Effective
                        : VisibleAs(component, accept.Package) ?? Visibility.Hidden;

                    // A wildcard is a blanket statement about whatever happens to be there, so it takes what
                    // it is given; naming a component outright is asking for that one, and asking for more
                    // than it offers is the error.
                    if (specificity == 2 && Reach(accept.Visibility) > Reach(offered))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3040,
                            $"An xsl:accept takes the {component.Kind} '{component.Name.LocalName}' as "
                            + $"\"{Spelling(accept.Visibility)}\", and the package it comes from offers it "
                            + $"as \"{Spelling(offered)}\". A using package may want less of a "
                            + "component than it was offered and cannot award itself more.");
                    }

                    // Abstract is a fact about the declaration and not a degree of visibility, so it can be
                    // said of a component only by an xsl:accept naming one that is.
                    if (specificity == 2
                        && accept.Visibility == Visibility.Abstract
                        && offered != Visibility.Abstract)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3040,
                            $"An xsl:accept takes the {component.Kind} '{component.Name.LocalName}' as "
                            + "abstract, and it is not: the package it comes from defines it. A component is "
                            + "abstract by being declared without a body, which is not something the "
                            + "package accepting it can decide.");
                    }
                }

                // A component the same xsl:use-package overrides is one this package has redefined, and an
                // xsl:accept naming it would be saying what to make of a component the override has
                // already replaced. The two are about one symbolic name in one xsl:use-package, and the
                // specification refuses the pair rather than settling which of them wins. A wildcard is a
                // blanket statement and says nothing about any component in particular, so it is allowed.
                ModuleElement usePackage = new ModuleElement(
                    acceptance.Source.Tree, m_tree.ParentOf(acceptance.Source.Element));

                foreach (ExposedName named in accept.Names)
                {
                    if (named.IsWildcard)
                    {
                        continue;
                    }

                    foreach ((ModuleElement declared, ModuleElement owner) in m_overrideOf)
                    {
                        if (!owner.Equals(usePackage)
                            || !IsXsltElement(declared.Element, out string overriding)
                            || (accept.Component != "*" && accept.Component != overriding)
                            || GetAttribute(declared.Element, "name") is not string written)
                        {
                            continue;
                        }

                        ExpandedName replaced = ResolveQualifiedName(declared.Element, written);

                        if (replaced.NamespaceUri != named.NamespaceUri
                            || replaced.LocalName != named.LocalName)
                        {
                            continue;
                        }

                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3051,
                            $"An xsl:accept names the {overriding} '{written}', and an xsl:override of the "
                            + "same xsl:use-package declares it. A component cannot be both taken as the "
                            + "package it comes from wrote it and replaced by one this package writes.");
                    }
                }

                foreach (ExposedName name in accept.Names)
                {
                    if (name.IsWildcard
                        || m_components.Exists(component => Matches(accept, name, component, offeredToo: true)))
                    {
                        continue;
                    }

                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3030,
                        $"An xsl:accept names {Written(name)} as {Article(accept.Component)} of the package "
                        + "it uses, and that package has no such component.");
                }
            }
        }

        // ---- What one package may see of another ---------------------------------------------------------

        /// <summary>Builds a component's record for the declaration being read.</summary>
        /// <param name="kind">What sort of component it is, spelled as <c>xsl:expose</c> spells it.</param>
        /// <param name="name">The component's name.</param>
        /// <param name="arity">How many parameters, for a function; -1 otherwise.</param>
        /// <param name="element">The declaration.</param>
        /// <param name="apply">Records a visibility, where the component can hold one.</param>
        /// <param name="isParameter">Whether the declaration is an <c>xsl:param</c>.</param>
        private PackageComponent Component(
            string kind,
            ExpandedName name,
            int arity,
            int element,
            Action<Visibility>? apply = null,
            bool isParameter = false)
        {
            bool overriding = IsOverriding(element);

            if (overriding)
            {
                m_overridingKeys.Add((m_package, kind, name, arity));
            }

            // A stylesheet parameter is public unless it says otherwise, the other way round from every
            // other component: it exists to be set from outside, and a package that used another could not
            // set the used one's parameters if they were kept in by default.
            Visibility? declared = DeclaredVisibility(element) ?? (isParameter ? Visibility.Public : null);

            return new PackageComponent(kind, name, arity, declared, apply, m_package)
            {
                IsOverride = overriding,
                IsParameter = isParameter,
                Element = new ModuleElement(m_tree, element),
            };
        }

        /// <summary>Whether a declaration in the module being read stood in an <c>xsl:override</c>.</summary>
        private bool IsOverriding(int element) => m_overrideOf.ContainsKey(new ModuleElement(m_tree, element));

        /// <summary>The package a module was read as part of.</summary>
        /// <remarks>
        /// A module not yet recorded is one still being gathered, and the package being read is its package.
        /// </remarks>
        private int PackageOf(XdmTree module)
        {
            return m_packageOfModule.TryGetValue(module, out int package) ? package : m_package;
        }

        /// <summary>The package whose module is being compiled.</summary>
        internal int CurrentPackage => PackageOf(m_tree);

        private readonly Dictionary<int, Dictionary<(ExpandedName Name, int Arity), UserFunction>>
            m_functionsSeenByPackage = new();

        /// <summary>
        /// The functions a package can see, which is what a <c>function-lookup()</c> written in it may find.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Not the same set as every function the compilation declared, which is what the table keyed by
        /// name and arity holds. A package sees its own functions whatever visibility they carry, and of
        /// another package's only what that package offers it — so a library's private function does not
        /// exist from outside the library, and a using package's functions do not exist from inside it. An
        /// abstract declaration is left out of both: it names a component with no implementation, and there
        /// is nothing to hand back for one.
        /// </para>
        /// <para>
        /// Built from the components rather than from the table, because the table holds one entry per name
        /// and arity: where a package overrides another's function that entry is the override, and what the
        /// used package sees under that name is still its own declaration. Safe to build when a body is
        /// compiled, every component having been declared before the first body is.
        /// </para>
        /// </remarks>
        /// <param name="package">The package the call is written in.</param>
        private IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> FunctionsSeenIn(int package)
        {
            if (m_functionsSeenByPackage.TryGetValue(package, out Dictionary<(ExpandedName, int), UserFunction>? seen))
            {
                return seen;
            }

            seen = new Dictionary<(ExpandedName, int), UserFunction>();

            foreach (PackageComponent component in m_components)
            {
                if (component.Kind == "function"
                    && component.Function is UserFunction function
                    && VisibleAs(component, package) is Visibility visibility
                    && visibility is not (Visibility.Hidden or Visibility.Abstract))
                {
                    seen[(component.Name, component.Arity)] = function;
                }
            }

            m_functionsSeenByPackage[package] = seen;
            return seen;
        }

        /// <summary>
        /// Marks a name as belonging to one package, for the declarations XSLT 3.0 makes local to a package
        /// (§3.5.3): decimal formats, keys, namespace aliases, character maps and output definitions.
        /// </summary>
        /// <remarks>
        /// The tables keyed by name are shared by every package the compilation flattens, and a library's
        /// <c>xsl:key name="k"</c> is not the principal's. Rather than a second key on every table, the
        /// name is marked with the package it belongs to, in a spelling no namespace can have; the
        /// top-level package's names stay as written, so nothing changes for a stylesheet that uses no
        /// package at all.
        /// </remarks>
        private ExpandedName Scoped(ExpandedName name) => ScopedIn(CurrentPackage, name);

        private static ExpandedName ScopedIn(int package, ExpandedName name)
        {
            return package == 0
                ? name
                : new ExpandedName(ScopeMark + package.ToString(CultureInfo.InvariantCulture) + ScopeMark + name.NamespaceUri, name.LocalName);
        }

        private string ScopedUri(string uri)
        {
            return CurrentPackage == 0
                ? uri
                : ScopeMark + CurrentPackage.ToString(CultureInfo.InvariantCulture) + ScopeMark + uri;
        }

        /// <summary>Whether a marked name belongs to a package, and the name as written if it does.</summary>
        private static bool InPackage(ExpandedName scoped, int package, out ExpandedName name)
        {
            string prefix = package == 0
                ? string.Empty
                : ScopeMark + package.ToString(CultureInfo.InvariantCulture) + ScopeMark;

            if (package == 0)
            {
                name = scoped;
                return scoped.NamespaceUri.Length == 0 || scoped.NamespaceUri[0] != ScopeMark;
            }

            if (scoped.NamespaceUri.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = new ExpandedName(scoped.NamespaceUri[prefix.Length..], scoped.LocalName);
                return true;
            }

            name = default;
            return false;
        }

        private const char ScopeMark = '\u0001';

        /// <summary>The decimal formats a package declares, by the names it declared them under.</summary>
        private Dictionary<ExpandedName, DecimalFormat> DecimalFormatsOf(int package)
        {
            Dictionary<ExpandedName, DecimalFormat> formats = new Dictionary<ExpandedName, DecimalFormat>();

            foreach ((ExpandedName scoped, DecimalFormat format) in m_decimalFormats)
            {
                if (InPackage(scoped, package, out ExpandedName name))
                {
                    formats[name] = format;
                }
            }

            return formats;
        }

        /// <summary>
        /// The unnamed output definition of a package: the principal result's for the top-level package,
        /// and a library's own for the result documents its instructions write without naming a format.
        /// </summary>
        private OutputSettings OutputSettingsFor(int package)
        {
            if (package == 0)
            {
                return m_outputSettings;
            }

            if (!m_libraryOutputSettings.TryGetValue(package, out OutputSettings? settings))
            {
                m_libraryOutputSettings[package] = settings = new OutputSettings();
            }

            return settings;
        }

        private readonly Dictionary<int, OutputSettings> m_libraryOutputSettings = new();

        /// <summary>
        /// The static context a computed decimal format name is resolved in when the call runs: the
        /// prefixes in scope where it was written, and the formats of the package it was written in.
        /// </summary>
        private sealed class DecimalFormatScope : IXPathStaticContext
        {
            private readonly Dictionary<string, string> m_prefixes;
            private readonly Dictionary<ExpandedName, DecimalFormat> m_formats;

            public DecimalFormatScope(
                NameSlotTable names,
                XsltVersion version,
                XsltVersion syntaxVersion,
                Dictionary<string, string> prefixes,
                Dictionary<ExpandedName, DecimalFormat> formats)
            {
                Names = names;
                Version = version;
                SyntaxVersion = syntaxVersion;
                m_prefixes = prefixes;
                m_formats = formats;
            }

            public NameSlotTable Names { get; }

            public XsltVersion Version { get; }

            public XsltVersion SyntaxVersion { get; }

            public string? ResolvePrefix(string prefix)
            {
                return m_prefixes.TryGetValue(prefix, out string? uri)
                    ? uri
                    : XPathStaticContext.PredeclaredPrefix(prefix, Version);
            }

            public bool TryResolveVariable(string namespaceUri, string localName, out int slot, out bool isGlobal)
            {
                slot = 0;
                isGlobal = false;
                return false;
            }

            public DecimalFormat? ResolveDecimalFormat(ExpandedName name)
            {
                if (m_formats.TryGetValue(name, out DecimalFormat? format))
                {
                    return format;
                }

                return name.LocalName.Length == 0 ? DecimalFormat.Default : null;
            }
        }

        /// <summary>The component of a given kind, name and arity that a package declares, if it does.</summary>
        private PackageComponent? FindComponent(string kind, ExpandedName name, int arity, int package)
        {
            foreach (PackageComponent component in m_components)
            {
                if (component.Package == package
                    && component.Kind == kind
                    && component.Arity == arity
                    && component.Name.Equals(name))
                {
                    return component;
                }
            }

            return null;
        }

        /// <summary>
        /// The visibility a component has from inside one package, or null where the package cannot see it
        /// at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A component is visible in the package that declares it, whatever its visibility, and in a package
        /// that uses that one — but only if the used package <em>offers</em> it, which is to say holds it as
        /// public, final or abstract, and then with the visibility the using package's <c>xsl:accept</c>
        /// gives it or a default that keeps it to itself. Followed through a chain of packages, each step
        /// asking the next what it holds the component as.
        /// </para>
        /// <para>
        /// The default matters more than it looks. A public component accepted without a word becomes
        /// <em>private</em> in the package that took it, so using a package does not re-offer what it
        /// offers; a package wanting to pass a component on has to say so in an <c>xsl:accept</c>.
        /// </para>
        /// </remarks>
        /// <param name="component">The component.</param>
        /// <param name="package">The package looking.</param>
        /// <param name="depth">How many uses have been followed, which bounds a cycle.</param>
        private Visibility? VisibleAs(PackageComponent component, int package, int depth = 0)
        {
            if (component.Package == package)
            {
                return component.Effective;
            }

            // A package that overrides a component has replaced it: what it holds under that name is its
            // own declaration, and the one it replaced is not in view there — nor, through it, anywhere
            // beyond. That is what makes an abstract component supplied, and a public one redefined.
            if (m_overridingKeys.Contains((package, component.Kind, component.Name, component.Arity)))
            {
                return null;
            }

            if (depth > m_packageCount || !m_uses.TryGetValue(package, out List<int>? uses))
            {
                return null;
            }

            foreach (int used in uses)
            {
                if (VisibleAs(component, used, depth + 1) is not Visibility offered
                    || offered is not (Visibility.Public or Visibility.Final or Visibility.Abstract))
                {
                    continue;
                }

                return AcceptedAs(component, used, package, offered);
            }

            return null;
        }

        /// <summary>What a package holds a component of a package it uses as.</summary>
        /// <param name="component">The component.</param>
        /// <param name="used">The package offering it.</param>
        /// <param name="package">The package taking it.</param>
        /// <param name="offered">The visibility it is offered with.</param>
        private Visibility AcceptedAs(PackageComponent component, int used, int package, Visibility offered)
        {
            Visibility? said = null;
            int best = -1;

            foreach (Acceptance acceptance in m_acceptances)
            {
                if (acceptance.Package != used || acceptance.Using != package)
                {
                    continue;
                }

                Exposure accept = AcceptanceOf(acceptance);

                // The later of two equal claims wins, as among the exposures: a package that uses another
                // twice, hiding a component the first time and taking it the second, means the second.
                if (BestMatch(accept, component, offeredToo: true) is int specificity
                    && Rank(accept, specificity) >= best)
                {
                    best = Rank(accept, specificity);
                    said = accept.Visibility;
                }
            }

            // Said nothing: a public component is taken as private, so that using a package does not
            // re-offer what it offers, and an abstract one is taken as absent, so that a library's
            // abstract function is a problem only for whoever reaches it, and only then. Taking it as
            // abstract has to be said in as many words, and then it is a problem for the package that
            // said so.
            return said ?? offered switch
            {
                Visibility.Public => Visibility.Private,
                Visibility.Abstract => Visibility.Absent,
                _ => offered,
            };
        }

        /// <summary>Reads one <c>xsl:accept</c>, once, leaving the module being compiled where it was.</summary>
        private Exposure AcceptanceOf(Acceptance acceptance)
        {
            if (m_readAcceptances.TryGetValue(acceptance, out Exposure? read))
            {
                return read;
            }

            XdmTree reading = m_tree;
            m_tree = acceptance.Source.Tree;

            try
            {
                read = ReadExposure(acceptance.Source.Element, accepting: true)
                    with { Package = acceptance.Package };
            }
            finally
            {
                m_tree = reading;
            }

            m_readAcceptances[acceptance] = read;
            return read;
        }

        /// <summary>Whether a package can refer to a component of a given kind, name and arity at all.</summary>
        /// <remarks>
        /// Hidden is invisible, and absent is not: an abstract component a package took without supplying
        /// is still there to be named, and an error only for whoever reaches it. What is refused here is a
        /// reference to something kept private by the package that defined it, or hidden outright by the
        /// package that took it, which from where the reference stands does not exist.
        /// </remarks>
        private bool IsReferable(string kind, ExpandedName name, int arity, int package)
        {
            foreach (PackageComponent component in m_components)
            {
                if (component.Kind == kind
                    && component.Arity == arity
                    && component.Name.Equals(name)
                    && VisibleAs(component, package) is Visibility seen
                    && seen != Visibility.Hidden)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Refuses a reference to a component the referring package cannot see, and a reference an
        /// executable package makes to one that is abstract.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The first is the whole of what visibility is for. A used package's private components are its
        /// own business, and a package using it that names one is naming something that, from where it
        /// stands, does not exist — so the answer is the one an unknown name gets, in whichever code that
        /// kind of reference has for it.
        /// </para>
        /// <para>
        /// The second is <c>XTSE3080</c>, and it turns on what the <em>principal</em> package holds the
        /// component as rather than on where the reference was written. A library that calls its own
        /// abstract function is fine as a library; taken into a package meant to be run, that call is a
        /// call to nothing unless the abstract component was supplied — or hidden, in which case it is
        /// absent, and calling it is a dynamic error for whoever reaches it.
        /// </para>
        /// </remarks>
        /// <param name="kind">What sort of component is referred to.</param>
        /// <param name="name">Its name.</param>
        /// <param name="arity">Its arity, for a function; -1 otherwise.</param>
        /// <param name="declaringPackage">The package whose declaration the reference resolved to.</param>
        /// <param name="invisible">The code for a reference to something that cannot be seen.</param>
        /// <param name="written">The reference as written, for the message.</param>
        private void CheckReference(
            string kind,
            ExpandedName name,
            int arity,
            int declaringPackage,
            XsltErrorCode invisible,
            string written)
        {
            int from = CurrentPackage;

            if (declaringPackage != from && !IsReferable(kind, name, arity, from))
            {
                throw XsltErrors.Error(
                    invisible,
                    $"'{written}' is {Article(kind)} of a package this one uses, and that package does not "
                    + "offer it. A component a package keeps private is one nothing outside the package can "
                    + "see, so from here it does not exist.");
            }

            // Only a package is "a package meant to be run". A plain stylesheet that declares something
            // abstract and reaches it hears about that when it is reached, as a dynamic error.
            if (m_isPackage
                && FindComponent(kind, name, arity, declaringPackage) is PackageComponent declared
                && declared.Effective == Visibility.Abstract
                && VisibleAs(declared, 0) == Visibility.Abstract)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE3080,
                    $"'{written}' is abstract, and nothing has supplied it. A package meant to be run cannot "
                    + "refer to a component that only names what some package using it was to define.");
            }
        }

        /// <summary>The package each named template was declared in, built once from the declarations.</summary>
        private Dictionary<Template, int>? m_templatePackages;

        /// <summary>The package a template was declared in.</summary>
        private int TemplatePackage(Template template)
        {
            if (m_templatePackages is null)
            {
                m_templatePackages = new Dictionary<Template, int>();

                foreach ((Template declared, ModuleElement source) in m_pendingTemplates)
                {
                    m_templatePackages[declared] = PackageOf(source.Tree);
                }
            }

            return m_templatePackages.TryGetValue(template, out int package) ? package : m_package;
        }

        /// <summary>The package a function was declared in.</summary>
        private int FunctionPackage(UserFunction function)
        {
            foreach ((UserFunction declared, ModuleElement source) in m_pendingFunctions)
            {
                if (ReferenceEquals(declared, function))
                {
                    return PackageOf(source.Tree);
                }
            }

            return m_package;
        }

        /// <summary>
        /// Whether a global variable resolved by name may be referred to from the module being compiled.
        /// </summary>
        /// <param name="name">The variable's name.</param>
        /// <param name="slot">The slot the name resolved to.</param>
        private bool GlobalIsVisible(ExpandedName name, int slot)
        {
            for (int i = 0; i < m_globals.Count; i++)
            {
                if (m_globals[i].Slot != slot)
                {
                    continue;
                }

                int declaringPackage = PackageOf(m_globalElements[i].Tree);

                if (declaringPackage != CurrentPackage && !IsReferable("variable", name, -1, CurrentPackage))
                {
                    return false;
                }

                if (m_isPackage
                    && FindComponent("variable", name, -1, declaringPackage) is PackageComponent declared
                    && declared.Effective == Visibility.Abstract
                    && VisibleAs(declared, 0) == Visibility.Abstract)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3080,
                        $"'${name.LocalName}' is abstract, and nothing has supplied it. A package meant to be "
                        + "run cannot refer to a component that only names what some package using it was to "
                        + "define.");
                }

                return true;
            }

            return true;
        }

        /// <summary>
        /// Checks every declaration an <c>xsl:override</c> made against the component it claims to replace.
        /// </summary>
        /// <remarks>
        /// Three things can be wrong with an override, and the specification gives each its code. It may
        /// name nothing the used package has (<c>XTSE3058</c>); it may name something the used package does
        /// not offer for overriding — a private or final component, or the unnamed mode, which is private
        /// to its package by definition (<c>XTSE3060</c>); or it may collide with another declaration of
        /// the same name in the package doing the overriding (<c>XTSE3055</c>), which is two answers to the
        /// question the override exists to settle.
        /// </remarks>
        private void CheckOverrides()
        {
            foreach ((ModuleElement declared, ModuleElement usePackage) in m_overrideOf)
            {
                if (!m_usePackageIds.TryGetValue(usePackage, out int used))
                {
                    continue;
                }

                m_tree = declared.Tree;
                m_scopeElement = declared.Element;
                int usingPackage = PackageOf(declared.Tree);

                foreach ((string kind, ExpandedName name, int arity, string written) in OverriddenKeys(declared.Element))
                {
                    if (kind == "mode" && name.LocalName.Length == 0)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3060,
                            "An xsl:override adds a template rule to the unnamed mode, which is private to "
                            + "the package it belongs to and cannot be overridden.");
                    }

                    // What the used package declares under the name, or failing that what it holds under
                    // it having accepted it from further down — an override reaches either.
                    PackageComponent? target = FindComponent(kind, name, arity, used)
                        ?? m_components.Find(component =>
                            component.Kind == kind
                            && component.Arity == arity
                            && component.Name.Equals(name)
                            && Offers(used, component));

                    if (target is null)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3058,
                            $"An xsl:override declares {Article(kind)} '{written}', and the package it "
                            + "uses has no such component to override.");
                    }

                    Visibility held = target.Package == used
                        ? target.Effective
                        : VisibleAs(target, used) ?? Visibility.Hidden;

                    if (held is not (Visibility.Public or Visibility.Abstract))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3060,
                            $"An xsl:override replaces {Article(kind)} '{written}', and the package it "
                            + $"comes from holds it as \"{Spelling(held)}\". Only a public or an "
                            + "abstract component may be overridden.");
                    }

                    RequireCompatible(kind, target, declared, written);

                    int declarations = 0;

                    foreach (PackageComponent component in m_components)
                    {
                        if (component.Package == usingPackage
                            && component.Kind == kind
                            && component.Arity == arity
                            && component.Name.Equals(name))
                        {
                            declarations++;
                        }
                    }

                    if (declarations > 1)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3055,
                            $"An xsl:override declares {Article(kind)} '{written}', and this package "
                            + "declares another of that name. An override settles what the name means here, "
                            + "and a second declaration would settle it again.");
                    }
                }
            }
        }

        /// <summary>The components one overriding declaration claims to replace.</summary>
        /// <remarks>
        /// A named template is one component; a template rule is a claim on each mode it is written for,
        /// which is a component of its own kind. A template with both a name and a match makes both claims.
        /// </remarks>
        /// <summary>
        /// Refuses an override whose signature is not compatible with the component it overrides
        /// (§3.5.3.2, <c>XTSE3070</c>): the same parameter and result types, the same say on the context
        /// item and on new-each-time, and nothing new required of a caller.
        /// </summary>
        private void RequireCompatible(string kind, PackageComponent target, ModuleElement overriding, string written)
        {
            if (target.Element is not ModuleElement overridden)
            {
                return;
            }

            string? why = kind switch
            {
                "function" => IncompatibleFunction(overridden, overriding),
                "template" => IncompatibleTemplate(overridden, overriding),
                "variable" => SameType(DeclaredTypeOf(overridden), DeclaredTypeOf(overriding))
                    ? null
                    : "the two declare different types",
                _ => null,
            };

            m_tree = overriding.Tree;
            m_scopeElement = overriding.Element;

            if (why is not null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE3070,
                    $"An xsl:override declares {Article(kind)} '{written}' whose signature is not compatible "
                    + $"with the one it overrides: {why}. An override presents the same interface as the "
                    + "component it replaces, so that nothing written against the original has to change.");
            }
        }

        private string? IncompatibleFunction(ModuleElement overridden, ModuleElement overriding)
        {
            List<XdmSequenceType?> before = ParameterTypesOf(overridden);
            List<XdmSequenceType?> after = ParameterTypesOf(overriding);

            for (int i = 0; i < before.Count && i < after.Count; i++)
            {
                if (!SameType(before[i], after[i]))
                {
                    return $"parameter {i + 1} is declared as '{Spelled(after[i])}' where the original "
                        + $"declares '{Spelled(before[i])}'";
                }
            }

            if (!SameType(DeclaredTypeOf(overridden), DeclaredTypeOf(overriding)))
            {
                return "the two declare different result types";
            }

            m_tree = overridden.Tree;
            bool? originally = ReadNewEachTime(overridden.Element);
            m_tree = overriding.Tree;

            return ReadNewEachTime(overriding.Element) == originally
                ? null
                : "the two say different things about new-each-time";
        }

        private string? IncompatibleTemplate(ModuleElement overridden, ModuleElement overriding)
        {
            if (!SameType(DeclaredTypeOf(overridden), DeclaredTypeOf(overriding)))
            {
                return "the two declare different result types";
            }

            List<(ExpandedName Name, XdmSequenceType? Type, bool Tunnel, bool Required)> before =
                TemplateParametersOf(overridden);
            List<(ExpandedName Name, XdmSequenceType? Type, bool Tunnel, bool Required)> after =
                TemplateParametersOf(overriding);

            foreach ((ExpandedName name, XdmSequenceType? type, bool tunnel, bool required) in before)
            {
                int at = after.FindIndex(parameter => parameter.Name.Equals(name));

                if (tunnel)
                {
                    // A tunnel parameter need not be declared again; declared again, it stays a tunnel
                    // parameter of the same type.
                    if (at < 0)
                    {
                        continue;
                    }

                    if (!after[at].Tunnel)
                    {
                        return $"'{name.LocalName}' is a tunnel parameter of the original and not of the override";
                    }

                    if (!SameType(type, after[at].Type))
                    {
                        return $"the tunnel parameter '{name.LocalName}' is declared as '{Spelled(after[at].Type)}' "
                            + $"where the original declares '{Spelled(type)}'";
                    }

                    continue;
                }

                if (at < 0 || after[at].Tunnel)
                {
                    return $"the original's parameter '{name.LocalName}' has no counterpart on the override";
                }

                if (!SameType(type, after[at].Type))
                {
                    return $"the parameter '{name.LocalName}' is declared as '{Spelled(after[at].Type)}' where "
                        + $"the original declares '{Spelled(type)}'";
                }

                if (required != after[at].Required)
                {
                    return $"the two say different things about whether '{name.LocalName}' is required";
                }
            }

            foreach ((ExpandedName name, _, _, bool required) in after)
            {
                if (required && !before.Exists(parameter => parameter.Name.Equals(name)))
                {
                    return $"the override adds a parameter '{name.LocalName}' and requires it, which no caller "
                        + "written against the original supplies";
                }
            }

            ContextItemDeclaration was = ContextItemOf(overridden);
            ContextItemDeclaration now = ContextItemOf(overriding);

            if (was.Use != now.Use)
            {
                return "the two say different things about whether the context item is used";
            }

            return SameType(was.Type, now.Type, "item()")
                ? null
                : "the two declare different types for the context item";
        }

        private XdmSequenceType? DeclaredTypeOf(ModuleElement declaration)
        {
            m_tree = declaration.Tree;
            return ReadDeclaredType(declaration.Element);
        }

        private List<XdmSequenceType?> ParameterTypesOf(ModuleElement declaration)
        {
            List<XdmSequenceType?> types = new();
            m_tree = declaration.Tree;

            for (int child = FirstIncludedChild(declaration.Element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string localName) && localName == "param")
                {
                    types.Add(ReadDeclaredType(child));
                }
            }

            return types;
        }

        private List<(ExpandedName Name, XdmSequenceType? Type, bool Tunnel, bool Required)> TemplateParametersOf(
            ModuleElement declaration)
        {
            List<(ExpandedName Name, XdmSequenceType? Type, bool Tunnel, bool Required)> parameters = new();
            m_tree = declaration.Tree;

            for (int child = FirstIncludedChild(declaration.Element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element
                    || !IsXsltElement(child, out string localName) || localName != "param")
                {
                    continue;
                }

                string name = GetAttribute(child, "name")
                    ?? throw new XsltException("An xsl:param must have a name.");

                parameters.Add((
                    ResolveQualifiedName(child, name),
                    ReadDeclaredType(child),
                    ReadDeclarationFlag(child, "tunnel"),
                    ReadDeclarationFlag(child, "required")));
            }

            return parameters;
        }

        private ContextItemDeclaration ContextItemOf(ModuleElement declaration)
        {
            m_tree = declaration.Tree;

            for (int child = FirstIncludedChild(declaration.Element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string localName) && localName == "context-item")
                {
                    return ReadContextItem(child);
                }
            }

            return ContextItemDeclaration.Default;
        }

        /// <summary>Whether two declared types are identical, an absent one being the default given.</summary>
        private bool SameType(XdmSequenceType? one, XdmSequenceType? other, string absent = "item()*")
        {
            one ??= XPathParser.ParseType(absent, this);
            other ??= XPathParser.ParseType(absent, this);

            return one.SameAs(other);
        }

        private static string Spelled(XdmSequenceType? type) => type?.ToString() ?? "item()*";

        private IEnumerable<(string Kind, ExpandedName Name, int Arity, string Written)> OverriddenKeys(int element)
        {
            if (!IsXsltElement(element, out string localName))
            {
                yield break;
            }

            string? name = GetAttribute(element, "name")?.Trim();

            switch (localName)
            {
                case "template":
                    if (name is not null)
                    {
                        yield return ("template", ResolveQualifiedName(element, name), -1, name);
                    }

                    if (GetAttribute(element, "match") is null)
                    {
                        yield break;
                    }

                    string[] modes = (GetAttribute(element, "mode") ?? "#default")
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string mode in modes)
                    {
                        if (mode == "#all")
                        {
                            continue;
                        }

                        // Through the same resolution a rule's mode gets anywhere else, so that a
                        // default-mode on the xsl:override or on the package is what "#default" means.
                        int resolved = ResolveMode(element, mode == "#default" ? null : mode);

                        yield return ("mode", ModeName(resolved), -1, mode);
                    }

                    yield break;

                case "function":
                {
                    int arity = 0;

                    for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
                    {
                        if (m_tree.KindOf(child) == NodeKind.Element
                            && IsXsltElement(child, out string kind)
                            && kind == "param")
                        {
                            arity++;
                        }
                    }

                    if (name is not null)
                    {
                        yield return ("function", ResolveQualifiedName(element, name), arity, $"{name}#{arity}");
                    }

                    yield break;
                }

                case "variable" or "param" when name is not null:
                    yield return ("variable", ResolveQualifiedName(element, name), -1, "$" + name);
                    yield break;

                case "attribute-set" when name is not null:
                    yield return ("attribute-set", ResolveQualifiedName(element, name), -1, name);
                    yield break;
            }
        }

        /// <summary>The name of a mode, by its number; the empty name for the unnamed mode.</summary>
        private ExpandedName ModeName(int mode)
        {
            foreach ((ExpandedName name, int id) in m_modes)
            {
                if (id == mode)
                {
                    return name;
                }
            }

            return new ExpandedName(string.Empty, string.Empty);
        }

        /// <summary>
        /// Refuses a package that ends up with two components of one name it can see and has not hidden.
        /// </summary>
        /// <remarks>
        /// <c>XTSE3050</c>. Two used packages offering a component of the same name, or one offering what
        /// the using package also declares for itself, leave a reference to that name with two things it
        /// could mean. An <c>xsl:override</c> is the one way of saying which: the accepted component is
        /// then the one replaced, and does not count. Neither does one the using package hid.
        /// </remarks>
        private void CheckHomonyms()
        {
            for (int package = 0; package <= m_packageCount; package++)
            {
                Dictionary<(string, ExpandedName, int), int> counts = new();
                HashSet<(int, string, ExpandedName, int)> seen = new();

                foreach (PackageComponent component in m_components)
                {
                    (string, ExpandedName, int) key = (component.Kind, component.Name, component.Arity);

                    // A name counts once per package that declares it, however many declarations it took:
                    // an import redeclaring a template, or an attribute set declared in two pieces, is one
                    // component with precedence or merging to settle it, and not what this is about.
                    if (component.IsParameter || !seen.Add((component.Package, component.Kind, component.Name, component.Arity)))
                    {
                        continue;
                    }

                    if (component.Package == package)
                    {
                        counts[key] = counts.GetValueOrDefault(key) + 1;
                        continue;
                    }

                    if (m_overridingKeys.Contains((package, component.Kind, component.Name, component.Arity)))
                    {
                        continue;
                    }

                    if (VisibleAs(component, package) is Visibility visible
                        && visible is not (Visibility.Hidden or Visibility.Absent))
                    {
                        counts[key] = counts.GetValueOrDefault(key) + 1;
                    }
                }

                foreach (((string kind, ExpandedName name, int arity), int count) in counts)
                {
                    if (count > 1)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3050,
                            $"This package can see two {kind} components named '{name.LocalName}'"
                            + (arity >= 0 ? $" with {arity} parameter(s)" : string.Empty)
                            + ", and has hidden neither. A reference to the name would have two things it "
                            + "could mean; an xsl:override says which, and an xsl:accept can hide one.");
                    }
                }
            }
        }

        /// <summary>
        /// Refuses a top-level package holding an abstract component that nothing supplied — declared by
        /// it, or accepted as abstract from a package it uses — whether or not anything refers to it
        /// (§3.5.3.3, <c>XTSE3080</c>).
        /// </summary>
        /// <remarks>
        /// A library may hold one: its abstract components are what a package using it is to define. A
        /// package meant to be run has no one left to define them. A plain stylesheet that declares one is
        /// still let be, and hears about it only if something reaches the component: that is the older
        /// arrangement, and nothing about a stylesheet says it is meant as a package.
        /// </remarks>
        private void CheckNothingAbstract()
        {
            if (!m_isPackage)
            {
                return;
            }

            foreach (PackageComponent component in m_components)
            {
                if (component.Effective != Visibility.Abstract
                    || m_overridingKeys.Contains((0, component.Kind, component.Name, component.Arity))
                    || VisibleAs(component, 0) != Visibility.Abstract)
                {
                    continue;
                }

                throw XsltErrors.Error(
                    XsltErrorCode.XTSE3080,
                    $"'{component.Name.LocalName}' is an abstract {component.Kind}, and nothing has supplied "
                    + "it. A package meant to be run cannot hold a component that only names what some "
                    + "package using it was to define; a library may, and the package using the library "
                    + "supplies it.");
            }
        }

        /// <summary>
        /// Refuses an <c>xsl:expose</c> that would give a component a visibility it may not have.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two rules, and the second is the first written the other way about. A declaration that stated a
        /// visibility has already said what it offers, and an <c>xsl:expose</c> naming it and saying
        /// something else is two answers to one question rather than a refinement of it. And
        /// <c>abstract</c> is not something an <c>xsl:expose</c> can confer at all: a component is abstract
        /// by being declared with no body for a using package to supply, which is a fact about the
        /// declaration and not about who may see it.
        /// </para>
        /// <para>
        /// Only an exact name is a disagreement. <c>names="*"</c> is the ordinary way to write "everything
        /// this package has, unless it says otherwise", and half the library packages in the test suite open
        /// with exactly that over a private template of their own — so reading a wildcard as a contradiction
        /// would refuse them. A wildcard still cannot confer <c>abstract</c>, which is not a visibility to be
        /// broadly granted but a statement that the declaration has no body.
        /// </para>
        /// </remarks>
        /// <param name="exposure">The declaration doing the exposing.</param>
        /// <param name="component">The component it reached.</param>
        /// <param name="exact">Whether the name that reached it was written out rather than a wildcard.</param>
        private static void CheckVisibilityChange(Exposure exposure, PackageComponent component, bool exact)
        {
            if (component.Declared is Visibility declared)
            {
                if (exact && !MayExpose(declared, exposure.Visibility))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3010,
                        $"The {component.Kind} '{component.Name.LocalName}' is declared "
                        + $"visibility=\"{Spelling(declared)}\", and an xsl:expose names it and says "
                        + $"\"{Spelling(exposure.Visibility)}\". An xsl:expose may hold a component back "
                        + "from what its declaration offers; it cannot hand out more than the declaration "
                        + "offers, and it cannot make a component abstract or stop one being so.");
                }

                return;
            }

            if (exposure.Visibility == Visibility.Abstract)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE3025,
                    $"An xsl:expose makes the {component.Kind} '{component.Name.LocalName}' abstract, and "
                    + "its declaration does not. A component is abstract by being declared with no body for "
                    + "a using package to supply, which is not something xsl:expose can confer.");
            }
        }

        /// <summary>
        /// Whether an <c>xsl:expose</c> naming a component outright may give it a visibility.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Withdrawing is allowed and granting is not: a package may say a component is public and an
        /// xsl:expose may then keep it in, but an xsl:expose cannot let out what the declaration kept.
        /// Private is the least and public the most, with final between them — final reaches as far as
        /// public and offers less, being public that may not be overridden.
        /// </para>
        /// <para>
        /// Abstract is not on that scale at all. It says the component has nothing in it and a using package
        /// must supply one, which is a fact about the declaration rather than about who may see it, so it
        /// can be neither conferred nor taken away and the only thing an xsl:expose may say about an
        /// abstract component is that it is abstract.
        /// </para>
        /// </remarks>
        /// <param name="declared">What the declaration said.</param>
        /// <param name="exposed">What the xsl:expose says.</param>
        private static bool MayExpose(Visibility declared, Visibility exposed)
        {
            if (declared == Visibility.Abstract || exposed == Visibility.Abstract)
            {
                return declared == exposed;
            }

            return Reach(exposed) <= Reach(declared);
        }

        /// <summary>
        /// How far a visibility lets a component travel, for comparing one with another.
        /// </summary>
        /// <remarks>
        /// Only meaningful between the four that are on the scale. Abstract is not one of them, and every
        /// caller settles what to do about it before asking.
        /// </remarks>
        /// <param name="visibility">The visibility to rank.</param>
        private static int Reach(Visibility visibility)
        {
            return visibility switch
            {
                Visibility.Hidden or Visibility.Absent => 0,
                Visibility.Private => 1,
                Visibility.Final => 2,
                _ => 3,
            };
        }

        /// <summary>
        /// How strong a claim an exposure that matched a component has on it, where several did.
        /// </summary>
        /// <remarks>
        /// The name decides first — an exact name over a namespace wildcard over a bare one — and the kind
        /// decides between names of one strength: <c>component="template"</c> over <c>component="*"</c>.
        /// Two that are equal on both leave the later one standing, which is what lets a package open with
        /// a blanket statement and then say otherwise about one thing.
        /// </remarks>
        /// <param name="exposure">The declaration that matched.</param>
        /// <param name="specificity">How specific the name that matched was.</param>
        private static int Rank(Exposure exposure, int specificity)
        {
            return specificity * 2 + (exposure.Component == "*" ? 0 : 1);
        }

        /// <summary>How specific the closest of an exposure's names is for a component, or null for no match.</summary>
        /// <param name="exposure">The <c>xsl:expose</c> or <c>xsl:accept</c>.</param>
        /// <param name="component">The component.</param>
        /// <param name="offeredToo">
        /// Whether a component the exposure's package did not declare but holds — one it accepted from a
        /// package it uses — is in reach as well, which it is for an <c>xsl:accept</c>.
        /// </param>
        private int? BestMatch(Exposure exposure, PackageComponent component, bool offeredToo = false)
        {
            int best = -1;

            foreach (ExposedName name in exposure.Names)
            {
                if (Matches(exposure, name, component, offeredToo) && name.Specificity > best)
                {
                    best = name.Specificity;
                }
            }

            return best < 0 ? null : best;
        }

        /// <summary>
        /// Whether a package holds a component it did not declare: one it accepted from a package it uses,
        /// and holds as public, final or abstract.
        /// </summary>
        private bool Offers(int package, PackageComponent component)
        {
            return component.Package != package
                && VisibleAs(component, package)
                    is Visibility.Public or Visibility.Final or Visibility.Abstract;
        }

        /// <summary>Whether one name in an exposure names one component.</summary>
        private bool Matches(Exposure exposure, ExposedName name, PackageComponent component, bool offeredToo = false)
        {
            if (component.IsParameter)
            {
                return false;
            }

            if (exposure.Package != component.Package && !(offeredToo && Offers(exposure.Package, component)))
            {
                return false;
            }

            if (exposure.Component != "*" && exposure.Component != component.Kind)
            {
                return false;
            }

            if (name.NamespaceUri is not null && name.NamespaceUri != component.Name.NamespaceUri)
            {
                return false;
            }

            if (name.LocalName is not null && name.LocalName != component.Name.LocalName)
            {
                return false;
            }

            if (component.Kind != "function")
            {
                // Only a function has an arity, so a name that gives one names nothing else. That is what
                // makes names="t1#0" on a template a name matching no component rather than a syntax error.
                return name.Arity < 0;
            }

            // Erratum E36: a function is identified by its name and its arity together, so an exact name
            // giving no arity identifies no function. A wildcard is a different sort of thing and goes on
            // matching by name alone.
            if (!name.IsWildcard && name.Arity < 0)
            {
                return false;
            }

            return name.Arity < 0 || name.Arity == component.Arity;
        }

        /// <summary>Reads an <c>xsl:expose</c> or <c>xsl:accept</c>, refusing what its attributes may not say.</summary>
        /// <param name="element">The element.</param>
        /// <param name="accepting">Whether it is an <c>xsl:accept</c>, which spells two things differently.</param>
        private Exposure ReadExposure(int element, bool accepting = false)
        {
            string what = accepting ? "xsl:accept" : "xsl:expose";
            string component = GetAttribute(element, "component")?.Trim()
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"An {what} must say which kind of component it is about, in a 'component' attribute.");

            if (component is not ("template" or "function" or "attribute-set" or "variable" or "mode" or "*"))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{component}' is not a kind of component a package offers. Only a template, a "
                    + "function, an attribute set, a variable or a mode is one, and '*' means every kind "
                    + "of them at once.");
            }

            string said = GetAttribute(element, "visibility")?.Trim()
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"An {what} must say what visibility it is giving, in a 'visibility' attribute.");

            // The two lists differ by one word. An xsl:expose cannot say 'hidden', which is a using
            // package's account of a component it has taken and does not want to see. An xsl:accept may
            // say 'abstract', but only of a component that is: abstract is a fact about a declaration
            // rather than a degree of visibility, and CheckAcceptances refuses the other case.
            Visibility visibility = said switch
            {
                "public" => Visibility.Public,
                "private" => Visibility.Private,
                "final" => Visibility.Final,
                "abstract" => Visibility.Abstract,
                "hidden" when accepting => Visibility.Hidden,
                "absent" when accepting => Visibility.Absent,
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{said}' is not a visibility {what} may give. It takes a component as public, "
                    + (accepting ? "private, final, abstract or hidden." : "private, final or abstract.")),
            };

            string names = GetAttribute(element, "names")
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"An {what} must say which components it is about, in a 'names' attribute.");

            List<ExposedName> parsed = new();

            foreach (string token in names.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                ExposedName name = ReadExposedName(element, token);

                // A component of every kind at once can only be named by a wildcard: the kinds have name
                // spaces of their own, so an exact name would be asking for a template and a variable and a
                // mode all called the same thing, which is not what anybody writing it means.
                if (component == "*" && !name.IsWildcard)
                {
                    throw XsltErrors.Error(
                        accepting ? XsltErrorCode.XTSE3032 : XsltErrorCode.XTSE3022,
                        $"{what} says component=\"*\" and names '{token}'. Every kind of component at once "
                        + "can only be named by a wildcard.");
                }

                parsed.Add(name);
            }

            return new Exposure
            {
                Component = component,
                Visibility = visibility,
                Names = parsed.ToArray(),
            };
        }

        /// <summary>
        /// Reads one name from an <c>xsl:expose</c>, which may be a wildcard and may carry an arity.
        /// </summary>
        /// <remarks>
        /// Everything refused here is <c>XTSE0020</c> rather than a name that matches nothing, because what
        /// is wrong is the attribute's own syntax. That includes <c>#unnamed</c> and its relatives: they are
        /// how <c>xsl:template</c> and <c>xsl:apply-templates</c> name the mode with no name, and the mode
        /// with no name is never a component, so writing one here is not a name that happens to match
        /// nothing but a token with no meaning in this attribute at all.
        /// </remarks>
        /// <param name="element">The <c>xsl:expose</c> element, whose namespaces a prefix resolves against.</param>
        /// <param name="token">One name as written.</param>
        private ExposedName ReadExposedName(int element, string token)
        {
            if (token.StartsWith("#", StringComparison.Ordinal))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{token}' is not a component name. The tokens beginning with '#' name modes in "
                    + "xsl:template and xsl:apply-templates, and the unnamed mode is not a component a "
                    + "package can offer at all.");
            }

            string text = token;
            int arity = -1;
            int hash = text.IndexOf('#');

            if (hash > 0)
            {
                if (!int.TryParse(text[(hash + 1)..], out arity) || arity < 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"'{token}' names a function, and what follows the '#' is not a number of "
                        + "parameters.");
                }

                text = text[..hash];
            }

            // The braced form of an EQName writes the namespace out rather than naming a prefix for it,
            // which is the only way to name a namespace that has no prefix in scope here.
            if (text.StartsWith("Q{", StringComparison.Ordinal))
            {
                int close = text.IndexOf('}');

                if (close < 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"'{token}' opens a braced namespace and does not close it.");
                }

                string local = text[(close + 1)..];

                return new ExposedName(text[2..close], local == "*" ? null : local, arity);
            }

            if (text == "*")
            {
                return new ExposedName(null, null, arity);
            }

            if (text.StartsWith("*:", StringComparison.Ordinal))
            {
                return new ExposedName(null, text[2..], arity);
            }

            if (text.EndsWith(":*", StringComparison.Ordinal))
            {
                string prefix = text[..^2];

                return new ExposedName(
                    m_tree.ResolvePrefix(element, prefix)
                        ?? throw XsltErrors.Error(
                            XsltErrorCode.XTSE0020,
                            $"'{token}' names components in the namespace bound to '{prefix}', and no "
                            + "namespace is bound to that prefix here."),
                    null,
                    arity);
            }

            int colon = text.IndexOf(':');

            if (colon < 0)
            {
                // No prefix means no namespace, whatever default namespace is in scope: a component name is
                // a QName in the XSLT sense, and those never take the default.
                return new ExposedName(string.Empty, text, arity);
            }

            return new ExposedName(
                m_tree.ResolvePrefix(element, text[..colon])
                    ?? throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"'{token}' uses the prefix '{text[..colon]}', and no namespace is bound to it "
                        + "here."),
                text[(colon + 1)..],
                arity);
        }

        /// <summary>Writes a name back out roughly as it was written, for an error to quote.</summary>
        /// <param name="name">The name to write.</param>
        private static string Written(ExposedName name)
        {
            string local = name.LocalName ?? "*";
            string written = name.NamespaceUri switch
            {
                null => "*:" + local,
                "" => local,
                string uri => "Q{" + uri + "}" + local,
            };

            return "'" + (name.Arity < 0 ? written : written + "#" + name.Arity) + "'";
        }

        /// <summary>Names a kind of component with the article that reads correctly before it.</summary>
        /// <param name="component">The kind, as the attribute spells it.</param>
        private static string Article(string component)
        {
            return component switch
            {
                "attribute-set" => "an attribute set",
                "*" => "a component",
                _ => "a " + component,
            };
        }

        /// <summary>Spells a visibility as a stylesheet writes it.</summary>
        /// <param name="visibility">The visibility to spell.</param>
        private static string Spelling(Visibility visibility)
        {
            return visibility switch
            {
                Visibility.Public => "public",
                Visibility.Private => "private",
                Visibility.Final => "final",
                Visibility.Abstract => "abstract",
                Visibility.Absent => "absent",
                _ => "hidden",
            };
        }

        /// <summary>
        /// Whether an XSLT 3.0 construct written here is one this processor will act on.
        /// </summary>
        /// <remarks>
        /// Two questions, and both have to be yes. The <em>stylesheet</em> must say 3.0, or it is asking for
        /// a language in which the construct does not exist. And the <em>processor</em> must claim 3.0, or a
        /// stylesheet saying 3.0 is being read forwards-compatibly — where an instruction this processor
        /// does not have is meant to be refused when reached and an <c>xsl:fallback</c> beside it taken.
        /// Implementing a 3.0 instruction and acting on it anyway would take that fallback away from every
        /// stylesheet that wrote one, which is not this engine's to do while a caller has asked it to be 2.0.
        /// </remarks>
        private bool Claims30(int element)
        {
            return m_options.Version.CompareTo(XsltVersion.V30) >= 0
                && VersionOf(element).CompareTo(XsltVersion.V30) >= 0;
        }

        /// <summary>
        /// Whether the <em>processor</em> implements XSLT 3.0, whatever version the stylesheet claims.
        /// </summary>
        /// <remarks>
        /// The line between this and <see cref="Claims30"/> is the line between vocabulary and behaviour. What
        /// attributes an element may carry is the vocabulary of the version the processor implements: a
        /// <c>version="2.0"</c> stylesheet is asking for backwards-compatible <em>behaviour</em>, not for a
        /// smaller set of attributes, and the suite says so — <c>html-version</c> on <c>xsl:output</c> and
        /// <c>new-each-time</c> on <c>xsl:function</c> are both written in 2.0 stylesheets and expected to be
        /// honoured. What changes for a construct that existed before, such as <c>xsl:copy</c> on an atomic
        /// value being a type error at 2.0 and a copy at 3.0, follows the stylesheet's own claim instead.
        /// </remarks>
        private bool Implements30 => m_options.Version.CompareTo(XsltVersion.V30) >= 0;

        /// <summary>
        /// Whether an element is one this processor reads. Vocabulary follows the processor, not the
        /// stylesheet's own version claim.
        /// </summary>
        /// <remarks>
        /// A <c>version="2.0"</c> stylesheet on a 3.0 processor is asking for backwards-compatible
        /// <em>behaviour</em>, not for a smaller language: the specification has it processed as a 3.0
        /// stylesheet with that behaviour switched on, so an <c>xsl:accumulator</c> in it is an accumulator.
        /// The suite writes such stylesheets and expects them read. Refusing them here was the one place the
        /// element vocabulary still followed the claim while the attribute vocabulary followed the processor.
        /// </remarks>
        /// <param name="shape">What the specification says about the element.</param>
        private bool IsAvailable(XsltElement shape)
        {
            return shape.Since.CompareTo(m_options.Version) <= 0;
        }

        /// <summary>The parameters of the <c>xsl:iterate</c> being compiled, which its body may rebind.</summary>
        /// <remarks>
        /// A stack because an iterate may stand inside another, and an <c>xsl:next-iteration</c> belongs to
        /// the nearest one enclosing it.
        /// </remarks>
        private readonly List<IterationParameter[]> m_iterations = new();

        /// <summary>
        /// Compiles <c>xsl:iterate</c>: its parameters, what it does when the sequence runs out, and its body.
        /// </summary>
        /// <remarks>
        /// The parameters are declared into scope before the body is compiled and stay there for the
        /// <c>xsl:on-completion</c> too, which is what lets the completion clause report on what the loop
        /// accumulated — the whole reason to prefer this to a recursive template.
        /// </remarks>
        private Instruction CompileIterate(int element)
        {
            Expr select = RequireExpression(element, "select");

            int scopeMark = m_scope.Count;
            List<IterationParameter> parameters = new List<IterationParameter>();

            // Looked for before the body is compiled, so that this is what a misplaced one hears rather than
            // whatever an xsl:break beside it has to say about its own position.
            RequireOnCompletionAtTop(element, element);
            List<Instruction> completion = new List<Instruction>();
            List<Instruction> body = new List<Instruction>();

            // Two passes, and the split is the point of it: the parameters have to be in scope and the loop
            // has to be on the stack before the body is compiled, because an xsl:next-iteration inside the
            // body resolves the names it rebinds against exactly those two things.
            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element || !IsXsltElement(child, out string localName))
                {
                    continue;
                }

                if (localName == "param")
                {
                    m_scopeElement = child;
                    string name = GetAttribute(child, "name")
                        ?? throw new XsltException("An xsl:param of an xsl:iterate must have a name.");

                    ExpandedName expanded = ResolveQualifiedName(child, name);
                    XdmSequenceType? type = ReadDeclaredType(child);
                    (Expr? initial, Instruction[]? content) = CompileVariableValue(child);

                    // A parameter with no value and a type that admits no empty sequence could only be
                    // supplied, and the first iteration has nobody to supply it: implicitly mandatory.
                    if (initial is null && content is null
                        && type is { Occurrence: XdmOccurrence.One or XdmOccurrence.OneOrMore })
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3520,
                            $"The xsl:param '{name}' of an xsl:iterate has no value and a type that allows no "
                            + "empty sequence, so its first iteration could never begin.");
                    }

                    int slot = m_frameSlotCount++;
                    parameters.Add(new IterationParameter(expanded, slot, initial, content, type));
                    m_scope.Add(new VariableBinding(expanded, slot, false));
                    continue;
                }

                if (localName == "on-completion")
                {
                    m_scopeElement = child;

                    if (GetAttribute(child, "select") is not null && HasContent(child))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3125,
                            "xsl:on-completion has both a select attribute and content. What it produces "
                            + "comes from one or the other.");
                    }

                    if (GetAttribute(child, "select") is string written)
                    {
                        completion.Add(new SequenceInstruction(ParseExpression(child, written)));
                    }
                    else
                    {
                        completion.AddRange(CompileSequence(child));
                    }

                    continue;
                }
            }

            m_iterations.Add(parameters.ToArray());

            try
            {
                body.AddRange(CompileSequence(element, name => name is "param" or "on-completion"));
            }
            finally
            {
                m_iterations.RemoveAt(m_iterations.Count - 1);
            }

            m_scope.RemoveRange(scopeMark, m_scope.Count - scopeMark);

            return new IterateInstruction(
                select, parameters.ToArray(), completion.ToArray(), body.ToArray());
        }

        /// <summary>Compiles <c>xsl:next-iteration</c>, resolving what it rebinds against the loop's slots.</summary>
        private Instruction CompileNextIteration(int element)
        {
            if (m_iterations.Count == 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0010,
                    "xsl:next-iteration stands outside any xsl:iterate, so there is no next iteration for it "
                    + "to start.");
            }

            IterationParameter[] declared = m_iterations[^1];
            List<(int Slot, Expr? Select, Instruction[]? Body, XdmSequenceType? Supplied,
                XdmSequenceType? Declared)> bindings = new();

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element
                    || !IsXsltElement(child, out string localName)
                    || localName != "with-param")
                {
                    continue;
                }

                m_scopeElement = child;
                string name = GetAttribute(child, "name")
                    ?? throw new XsltException("An xsl:with-param must have a name.");

                ExpandedName expanded = ResolveQualifiedName(child, name);
                IterationParameter? target = Array.Find(declared, p => p.Name.Equals(expanded));

                if (target is null)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3130,
                        $"xsl:next-iteration sets '{name}', which the xsl:iterate around it does not declare "
                        + "as a parameter.");
                }

                // Two parameters of one name on one xsl:next-iteration: the second would silently win, and
                // which of two values the iteration was meant to start again with is not a processor's to
                // decide. The same rule and the same code as on any other call.
                if (bindings.Exists(binding => binding.Slot == target.Slot))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0670,
                        $"'{name}' is supplied twice on one xsl:next-iteration. A parameter can be given "
                        + "one value.");
                }

                // Both types apply where both were written: the as on the xsl:with-param converts what the
                // call supplies, by the function conversion rules, and the as on the xsl:param says what the
                // parameter is declared to hold. So an xsl:param with no type of its own may still be
                // handed an xs:double* by the call that rebinds it.
                (Expr? select, Instruction[]? body) = CompileVariableValue(child);
                bindings.Add((target.Slot, select, body, ReadDeclaredType(child), target.Type));
            }

            return new NextIterationInstruction(bindings.ToArray());
        }

        /// <summary>
        /// Compiles <c>xsl:merge</c>, its sources and the action that runs once per group.
        /// </summary>
        /// <remarks>
        /// A source says where its items come from in one of three ways, and they are alternatives rather
        /// than a list: <c>for-each-item</c> gives a context per item, <c>for-each-source</c> a context per
        /// document, and neither means the <c>select</c> is evaluated once where the instruction stands.
        /// </remarks>
        private Instruction CompileMerge(int element)
        {
            List<MergeSource> sources = new List<MergeSource>();

            HashSet<ExpandedName> sourceNames = new HashSet<ExpandedName>();
            List<int> sourceElements = new List<int>();
            Instruction[]? action = null;

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element || !IsXsltElement(child, out string localName))
                {
                    continue;
                }

                m_scopeElement = child;

                if (localName == "merge-action")
                {
                    if (action is not null)
                    {
                        // One action for every group the merge forms; a second would be a second answer
                        // to what is done with a group, and nothing decides between them (XTSE0010).
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0010,
                            "xsl:merge has two xsl:merge-action children, and takes exactly one: it is what "
                            + "is done with each group the merge forms.");
                    }

                    action = CompileSequence(child);
                    continue;
                }

                if (localName != "merge-source")
                {
                    // xsl:fallback, which the content model admits after the action and which a processor
                    // that understands xsl:merge has no use for.
                    continue;
                }

                if (action is not null)
                {
                    // The sources come first and the action last, the content model being ordered: a
                    // source after the action is out of place (XTSE0010).
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0010,
                        "An xsl:merge-source comes after the xsl:merge-action. The sources are written "
                        + "first, and the action, which is what is done with each group they form, last.");
                }

                if (GetAttribute(child, "for-each-stream") is not null)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3430,
                        "xsl:merge-source uses for-each-stream, which reads a document without building it. "
                        + "This engine builds every document it reads; use for-each-source instead.");
                }

                // What the source promises about its own order. Saying nothing promises that the items
                // arrive sorted, and a source that then does not is XTDE2220 rather than something quietly
                // put right.
                bool sortFirst = ReadDeclarationFlag(child, "sort-before-merge");

                // A source says where to look and what to take from there. The two for-each attributes are
                // two answers to the first question, and the second has no default: a merge-source with no
                // select does not say what it contributes.
                if (GetAttribute(child, "for-each-item") is not null
                    && GetAttribute(child, "for-each-source") is not null)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3195,
                        "An xsl:merge-source has both 'for-each-item' and 'for-each-source'. Each says what "
                        + "the source is iterated over, and nothing decides between two of them.");
                }

                if (GetAttribute(child, "select") is not string selected)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0010,
                        "An xsl:merge-source must have a 'select', which is what it contributes to the "
                        + "merge.");
                }

                SortKey[] keys = CompileSortKeys(child, "merge-key");

                if (keys.Length == 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0010,
                        "An xsl:merge-source must have at least one xsl:merge-key. A merge walks its "
                        + "sources in step, and without a key there is nothing to keep them in step by.");
                }

                if (GetAttribute(child, "name") is string sourceName


                    && !sourceNames.Add(ResolveQualifiedName(child, sourceName)))


                {


                    throw XsltErrors.Error(


                        XsltErrorCode.XTSE3190,


                        $"Two xsl:merge-source elements of one xsl:merge are named '{sourceName}', and a name is how "


                        + "current-merge-group() tells them apart.");


                }



                sources.Add(new MergeSource(
                    GetAttribute(child, "name") is string named
                        ? ResolveQualifiedName(child, named)
                        : null,
                    GetAttribute(child, "for-each-item") is string each
                        ? ParseExpression(child, each)
                        : null,
                    GetAttribute(child, "for-each-source") is string from
                        ? ParseExpression(child, from)
                        : null,
                    ParseExpression(child, selected),
                    keys,
                    MergeKeyOrderings(child),
                    StaticBaseUri(child),
                    sortFirst,
                    ReadUseAccumulators(child)));

                sourceElements.Add(child);
            }

            RequireAgreeingKeys(sourceElements);

            if (sources.Count == 0 || action is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0010,
                    "xsl:merge needs at least one xsl:merge-source and an xsl:merge-action; without both "
                    + "there is nothing to merge or nothing to do with it.");
            }

            m_scopeElement = element;
            return new MergeInstruction(sources.ToArray(), action);
        }

        /// <summary>
        /// Refuses two <c>xsl:merge-source</c> that describe the ordering with different numbers of keys.
        /// </summary>
        /// <remarks>
        /// A merge walks its sources in step, which means comparing an item of one against an item of
        /// another; that comparison is one thing rather than one per source, so the key in a given position
        /// has to be the same key everywhere. Two sources with different numbers of them do not have
        /// corresponding keys to compare, and there is no sequence for the result to be in. What those
        /// corresponding keys then have to agree <em>about</em> is settled when the merge runs, their
        /// attributes being attribute value templates.
        /// </remarks>
        /// <param name="sources">The <c>xsl:merge-source</c> elements, in the order they were written.</param>
        private void RequireAgreeingKeys(List<int> sources)
        {
            for (int i = 1; i < sources.Count; i++)
            {
                int mine = MergeKeysOf(sources[0]).Count;
                int theirs = MergeKeysOf(sources[i]).Count;

                if (mine != theirs)
                {
                    m_scopeElement = sources[i];

                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE2200,
                        $"One xsl:merge-source has {mine} xsl:merge-key children and another has {theirs}. "
                        + "The sources are walked in step by comparing their keys in turn, so each of them "
                        + "has to describe the ordering with the same number of keys.");
                }
            }
        }

        /// <summary>The <c>xsl:merge-key</c> children of one <c>xsl:merge-source</c>, in order.</summary>
        private List<int> MergeKeysOf(int source)
        {
            List<int> keys = new List<int>();

            for (int child = FirstIncludedChild(source); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string localName)
                    && localName == "merge-key")
                {
                    keys.Add(child);
                }
            }

            return keys;
        }

        /// <summary>
        /// Reads what each <c>xsl:merge-key</c> of one source says about the ordering, as written.
        /// </summary>
        /// <remarks>
        /// Kept beside the compiled keys rather than folded into them, because these five attributes are
        /// attribute value templates: what they say is not settled until the merge runs, and the rule that
        /// two sources must agree about them is a rule about what they then said.
        /// </remarks>
        /// <param name="source">The <c>xsl:merge-source</c>.</param>
        private MergeKeyOrdering[] MergeKeyOrderings(int source)
        {
            List<int> elements = MergeKeysOf(source);
            MergeKeyOrdering[] ordering = new MergeKeyOrdering[elements.Count];

            for (int i = 0; i < elements.Count; i++)
            {
                ordering[i] = new MergeKeyOrdering(
                    OptionalAttributeValueTemplate(elements[i], "order"),
                    OptionalAttributeValueTemplate(elements[i], "data-type"),
                    OptionalAttributeValueTemplate(elements[i], "lang"),
                    OptionalAttributeValueTemplate(elements[i], "case-order"),
                    OptionalAttributeValueTemplate(elements[i], "collation"));
            }

            m_scopeElement = source;
            return ordering;
        }

        /// <summary>The <c>err:</c> variables an <c>xsl:catch</c> may read, in the order they are given slots.</summary>
        private static readonly string[] s_errorVariables =
        {
            "code", "description", "value", "module", "line-number", "column-number",
        };

        /// <summary>
        /// Compiles <c>xsl:evaluate</c>, which compiles its own expression later.
        /// </summary>
        /// <remarks>
        /// What is settled here is everything the target expression's static context will be built from
        /// that is known now: the prefixes in scope on the instruction — the default namespace left out,
        /// since the expression takes its default from <c>xpath-default-namespace</c> and not from the
        /// elements written around it — the instruction's own base URI, the version in force, and the
        /// tables of functions and decimal formats, which are complete by the time anything runs.
        /// </remarks>
        private Instruction CompileEvaluate(int element)
        {
            m_scopeElement = element;

            Expr xpath = RequireExpression(element, "xpath");
            XdmSequenceType? resultType = ReadDeclaredType(element);
            Expr? withParams = GetAttribute(element, "with-params") is string map
                ? ParseExpression(element, map)
                : null;
            Expr? contextItem = GetAttribute(element, "context-item") is string focus
                ? ParseExpression(element, focus)
                : null;
            Expr? namespaceContext = GetAttribute(element, "namespace-context") is string scope
                ? ParseExpression(element, scope)
                : null;
            AttributeValueTemplate? baseUri = OptionalAttributeValueTemplate(element, "base-uri");
            AttributeValueTemplate? schemaAware = OptionalAttributeValueTemplate(element, "schema-aware");
            WithParameter[] parameters = CompileWithParameters(element);

            List<Instruction>? fallback = null;

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string localName)
                    && localName == "fallback")
                {
                    (fallback ??= new List<Instruction>()).AddRange(CompileSequence(child));
                }
            }

            m_scopeElement = element;

            Dictionary<string, string> namespaces = NamespacesOn(element);
            namespaces.Remove(string.Empty);

            return new EvaluateInstruction(
                xpath,
                resultType,
                baseUri,
                StaticBaseUri(element),
                withParams,
                contextItem,
                namespaceContext,
                schemaAware,
                parameters,
                namespaces,
                DefaultElementNamespace,
                m_names,
                m_functions,
                DecimalFormatsOf(CurrentPackage),
                DefaultCollation,
                Version,
                m_options.DynamicEvaluation,
                fallback?.ToArray());
        }

        /// <summary>
        /// Compiles <c>xsl:try</c> and the <c>xsl:catch</c> clauses after it.
        /// </summary>
        /// <remarks>
        /// The <c>err:</c> variables are declared into the clause's own scope rather than being looked up by
        /// name at run time, which is what lets a clause that reads none of them cost nothing: the slots are
        /// allocated whichever way, but the binding is skipped where no expression in the clause resolved to
        /// one.
        /// </remarks>
        private Instruction CompileTry(int element)
        {
            Expr? select = GetAttribute(element, "select") is string written
                ? ParseExpression(element, written)
                : null;

            List<Instruction> body = new List<Instruction>();
            List<CatchClause> catches = new List<CatchClause>();

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string localName)
                    && localName == "catch")
                {
                    catches.Add(CompileCatch(child));
                }
            }

            body.AddRange(CompileSequence(element, name => name == "catch"));

            if (select is not null && body.Count > 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE3140,
                    "xsl:try has both a select expression and content of its own. Only one of them can say "
                    + "what it is trying to produce.");
            }

            if (catches.Count == 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0010,
                    "xsl:try has no xsl:catch, so there is nothing for it to do with an error.");
            }

            // rollback-output="no" gives up the buffer a rollback needs; the default is "yes".
            bool rollback = GetAttribute(element, "rollback-output") is not string rolled || IsYes(rolled);

            return new TryInstruction(select, body.ToArray(), catches.ToArray(), rollback);
        }

        /// <summary>Compiles one <c>xsl:catch</c>, with the error variables in scope inside it.</summary>
        private CatchClause CompileCatch(int element)
        {
            int scopeMark = m_scope.Count;
            int first = m_frameSlotCount;

            foreach (string name in s_errorVariables)
            {
                m_scope.Add(new VariableBinding(
                    new ExpandedName(TryInstruction.ErrorNamespace, name), m_frameSlotCount++, false));
            }

            try
            {
                m_scopeElement = element;

                Expr? select = GetAttribute(element, "select") is string written
                    ? ParseExpression(element, written)
                    : null;

                if (select is not null && HasContent(element))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3150,
                        "xsl:catch has both a select attribute and content. What it produces comes from one "
                        + "or the other.");
                }

                Instruction[] body = select is null ? CompileSequence(element) : Array.Empty<Instruction>();

                return new CatchClause(ReadCatchNameTests(element), select, body, first);
            }
            finally
            {
                m_scope.RemoveRange(scopeMark, m_scope.Count - scopeMark);
            }
        }

        /// <summary>
        /// Reads an <c>errors</c> attribute into the name tests an error code has to match.
        /// </summary>
        /// <returns>The tests, or null where the clause takes every error.</returns>
        private CatchNameTest[]? ReadCatchNameTests(int element)
        {
            string[] tokens = (GetAttribute(element, "errors") ?? "*")
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0 || Array.IndexOf(tokens, "*") >= 0)
            {
                return null;
            }

            CatchNameTest[] tests = new CatchNameTest[tokens.Length];

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int colon = token.IndexOf(':');

                if (colon < 0)
                {
                    // An unprefixed name in an error test is a name in no namespace, the same rule a
                    // variable's name follows: the default namespace is for elements and types alone.
                    tests[i] = new CatchNameTest(string.Empty, token);
                    continue;
                }

                string prefix = token[..colon];
                string local = token[(colon + 1)..];

                tests[i] = new CatchNameTest(
                    prefix == "*" ? null : ResolveQualifiedName(element, $"{prefix}:x").NamespaceUri,
                    local == "*" ? null : local);
            }

            return tests;
        }

        /// <summary>
        /// Settles what <c>xsl:global-context-item</c> says, once every module of every package is in.
        /// </summary>
        /// <remarks>
        /// One declaration per package and no more: a module carrying two is refused outright, and two
        /// modules of one package that disagree are refused as well, there being no rule for which of them
        /// the package meant (XSLT 3.0 §3.5.2). A library package's declaration is not the transformation's
        /// — the top-level package settles that — so it is passed over, unless it says the item is
        /// <em>required</em>, which is a promise a library is in no position to make.
        /// </remarks>
        private void SettleGlobalContextItem()
        {
            Dictionary<int, (XdmTree Module, ContextItemDeclaration Declared)> byPackage = new();

            foreach ((int package, XdmTree module, ContextItemDeclaration declared) in m_globalContextItems)
            {
                if (!byPackage.TryGetValue(package, out var earlier))
                {
                    byPackage.Add(package, (module, declared));
                    continue;
                }

                if (ReferenceEquals(earlier.Module, module))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3087,
                        "One stylesheet module declares xsl:global-context-item twice. One declaration says "
                        + "what the transformation is run against, and a second says it again or says "
                        + "something else.");
                }

                if (earlier.Declared.Use != declared.Use
                    || earlier.Declared.Type?.ToString() != declared.Type?.ToString())
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3087,
                        "Two modules of one package declare xsl:global-context-item and do not agree. "
                        + "Nothing says which of them the package meant.");
                }
            }

            foreach ((int package, (XdmTree _, ContextItemDeclaration declared)) in byPackage)
            {
                if (package == 0)
                {
                    m_globalContextItem = declared;
                    continue;
                }

                if (declared.Use == ContextItemUse.Required)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTTE0590,
                        "A library package declares that the transformation must be run against a context "
                        + "item. What it is run against is the top-level package's business, so a library "
                        + "asking for one is asking something it cannot be answered.");
                }
            }
        }

        /// <summary>
        /// Reads an <c>xsl:context-item</c> or <c>xsl:global-context-item</c> declaration.
        /// </summary>
        /// <remarks>
        /// The values are checked here rather than by the element table, which is switched off entirely in
        /// the forwards-compatible processing a <c>version="3.0"</c> stylesheet gets while this engine
        /// claims 2.0. Declaring <c>use="absent"</c> alongside an <c>as</c> is a contradiction — there is
        /// nothing for the type to be about — and the specification gives it a code of its own.
        /// </remarks>
        private ContextItemDeclaration ReadContextItem(int element, bool forStylesheet = false)
        {
            string said = GetAttribute(element, "use")?.Trim() ?? "optional";

            ContextItemUse use = said switch
            {
                "optional" => ContextItemUse.Optional,
                "required" => ContextItemUse.Required,
                "absent" => ContextItemUse.Absent,
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{said}' is not one of the values 'use' may take: optional, required, absent."),
            };

            XdmSequenceType? type;

            try
            {
                type = ReadDeclaredType(element);
            }
            catch (XsltException failed)
            {
                // Whatever the parser made of the text, what is wrong is the attribute, and the
                // specification's complaint about an attribute whose value is not a permitted one is what
                // it names here. A type nobody declared is the usual way to arrive: this engine is not
                // schema-aware, so element(*, my:percentage) names a type that cannot exist.
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{GetAttribute(element, "as")}' is not an item type, which is what the 'as' of "
                    + $"{QualifiedNameOf(element)} declares. {failed.Message}",
                    failed);
            }

            // An item type and not a sequence type: the context item is one item, so there is nothing for
            // an occurrence indicator to say about it. Writing one is writing about a sequence that cannot
            // be there.
            if (type is not null && type.Occurrence != XdmOccurrence.One)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{GetAttribute(element, "as")}' is a sequence type, and the 'as' of "
                    + $"{QualifiedNameOf(element)} takes an item type: the context item is one item, and an "
                    + "occurrence indicator has nothing to say about it.");
            }

            if (use == ContextItemUse.Absent && type is not null)
            {
                // Two codes for one mistake, the specification having given each element its own.
                throw XsltErrors.Error(
                    forStylesheet ? XsltErrorCode.XTSE3089 : XsltErrorCode.XTSE3088,
                    $"{QualifiedNameOf(element)} says the context item is absent and also declares a type "
                    + "for it. There is nothing for the type to be about.");
            }

            return new ContextItemDeclaration(use, type);
        }

        private TemplateParameter CompileTemplateParameter(int element)
        {
            m_scopeElement = element;

            string name = GetAttribute(element, "name")
                ?? throw new XsltException("An xsl:param must have a name.");

            if (GetAttribute(element, "static") is not null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0090,
                    $"The parameter '{name}' is declared static, and only a stylesheet parameter can be: a "
                    + "static parameter is settled before the stylesheet is compiled, which a template's is not.");
            }

            ExpandedName expanded = ResolveQualifiedName(element, name);
            XdmSequenceType? declared = ReadDeclaredType(element);
            (Expr? select, Instruction[]? body) = CompileVariableValue(element);
            bool required = ReadDeclarationFlag(element, "required");

            if (required && (select is not null || body is not null))
            {
                throw new XsltException(
                    $"The parameter '{name}' is declared required=\"yes\" and also given a default. A "
                    + "required parameter is one the caller must supply, so a default could never be used.");
            }

            int slot = m_frameSlotCount++;
            m_scope.Add(new VariableBinding(expanded, slot, false));

            return new TemplateParameter(
                expanded, slot, select, body, ReadDeclarationFlag(element, "tunnel"), required, declared)
            {
                BaseUri = StaticBaseUri(element),
            };
        }

        /// <summary>
        /// Reads an <c>as</c> attribute into the type it names.
        /// </summary>
        /// <remarks>
        /// A declared type is not only a check. It decides what a sequence constructor's content amounts to:
        /// without one the content is built into a single document node, and with one it stays the sequence
        /// the constructor produced.
        /// </remarks>
        private XdmSequenceType? ReadDeclaredType(int element)
        {
            string? declared = GetAttribute(element, "as");
            if (declared is null)
            {
                return null;
            }

            m_scopeElement = element;
            return XPathParser.ParseType(declared, this);
        }

        private (Expr? Select, Instruction[]? Body) CompileVariableValue(int element)
        {
            string? select = GetAttribute(element, "select");
            if (select is not null)
            {
                // A value comes from one or the other, and a binding written with both says two things —
                // which is XTSE0620 for a variable, a parameter and an xsl:with-param alike.
                return (CompileValueSelect(element), null);
            }

            if (FirstIncludedChild(element) < 0)
            {
                return (null, null);
            }

            return (null, CompileSequence(element));
        }

        /// <summary>
        /// Whether anything that would produce something follows a node among its siblings.
        /// </summary>
        /// <remarks>
        /// The layout whitespace between two instructions is not content — it is stripped from a stylesheet
        /// — so an instruction written last is still last with a newline after it.
        /// </remarks>
        private bool HasContentAfter(int node, Func<string, bool>? skip = null)
        {
            for (int next = NextIncludedSibling(node); next >= 0; next = NextIncludedSibling(next))
            {
                // A child the constructor leaves out — an xsl:catch after an xsl:try's body — is not content
                // after the xsl:on-empty, whatever its place in the element.
                if (skip is not null
                    && m_tree.KindOf(next) == NodeKind.Element
                    && IsXsltElement(next, out string skipped)
                    && skip(skipped))
                {
                    continue;
                }

                if (m_tree.KindOf(next) != NodeKind.Text
                    || !m_tree.StringValueOf(next).AsSpan().Trim(" \t\r\n".AsSpan()).IsEmpty)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The last of the leading declarations a parent reads for itself, or -1 where there are none.
        /// </summary>
        /// <remarks>
        /// The leading run only. An <c>xsl:catch</c> is left out of its <c>xsl:try</c>'s constructor too and
        /// stands at the end of it, so the scan stops at the first child that is neither one of these
        /// declarations nor the whitespace between them.
        /// </remarks>
        /// <param name="element">The element whose children are being read.</param>
        /// <param name="skip">Which of them the parent reads for itself.</param>
        private int LastLeading(int element, Func<string, bool> skip)
        {
            int last = -1;

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                NodeKind kind = m_tree.KindOf(child);

                if (kind == NodeKind.Element && IsXsltElement(child, out string name) && skip(name))
                {
                    last = child;
                    continue;
                }

                if (kind is NodeKind.Comment or NodeKind.ProcessingInstruction
                    || (kind == NodeKind.Text && m_tree.StringValueOf(child).AsSpan().IsWhiteSpace()))
                {
                    continue;
                }

                break;
            }

            return last;
        }

        /// <summary>
        /// Compiles a sequence constructor: the children of an element, in order, with an xsl:on-empty or
        /// xsl:on-non-empty among them making the whole a conditional sequence.
        /// </summary>
        /// <remarks>
        /// Every sequence constructor comes through here, whatever else its element holds — an xsl:for-each
        /// its sorts, a template its parameters, an xsl:try its catches — because xsl:on-empty asks about
        /// the constructor it stands in, and that is any of them.
        /// <para>
        /// Those two ask what the rest of <em>this</em> constructor produced, so the rest has to be buffered
        /// before either can be answered — which a flat list of instructions cannot express. The split is
        /// made in the same pass that compiles, and a constructor holding neither of them, which is nearly
        /// all of them, comes back as the flat list it always was.
        /// </para>
        /// </remarks>
        /// <param name="element">The element whose children are the constructor.</param>
        /// <param name="skip">Which XSLT children, by local name, are not part of the constructor.</param>
        private Instruction[] CompileSequence(int element, Func<string, bool>? skip = null)
        {
            // A variable declared in a sequence constructor is in scope for the following siblings of its
            // declaration and their descendants, and for nothing else (§9.7). So a constructor takes its
            // own bindings away with it: one declared inside a literal result element shadows nothing
            // after that element closes, and the name goes back to meaning whatever it meant before.
            int scopeMark = m_scope.Count;

            try
            {
                return CompileSequenceIn(element, skip);
            }
            finally
            {
                m_scope.RemoveRange(scopeMark, m_scope.Count - scopeMark);
            }
        }

        /// <summary>Compiles a sequence constructor, leaving the bindings it declared in scope.</summary>
        /// <param name="element">The element whose children are the constructor.</param>
        /// <param name="skip">Which XSLT children, by local name, are not part of the constructor.</param>
        private Instruction[] CompileSequenceIn(int element, Func<string, bool>? skip = null)
        {
            List<Instruction> instructions = new List<Instruction>();
            List<ConditionalSegment>? segments = null;
            int declarations = skip is null ? -1 : LastLeading(element, skip);

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (skip is not null
                    && m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string skipped)
                    && skip(skipped))
                {
                    continue;
                }

                // The constructor begins after those declarations, so the whitespace standing between and
                // before them is layout rather than content. It only reaches here at all under
                // xml:space="preserve", which is the one place the difference can be seen.
                if (child < declarations
                    && m_tree.KindOf(child) == NodeKind.Text
                    && m_tree.StringValueOf(child).AsSpan().IsWhiteSpace())
                {
                    continue;
                }

                if (m_tree.KindOf(child) != NodeKind.Element
                    || !IsXsltElement(child, out string localName)
                    || localName is not ("on-empty" or "on-non-empty")
                    || !Implements30)
                {
                    CompileNode(child, instructions);
                    continue;
                }

                segments ??= new List<ConditionalSegment>();

                if (instructions.Count > 0)
                {
                    segments.Add(new ConditionalSegment(instructions.ToArray(), null));
                    instructions.Clear();
                }

                m_scopeElement = child;

                Instruction[] body = GetAttribute(child, "select") is string written
                    ? new Instruction[] { new SequenceInstruction(ParseExpression(child, written)) }
                    : CompileSequence(child);

                segments.Add(new ConditionalSegment(body, localName == "on-non-empty"));

                // xsl:on-empty says what to write when the rest of this constructor produced nothing, and
                // "the rest" has to be all of it: written anywhere but last, it would be asking about only
                // the part above it and answering for the whole. The specification made that an error
                // rather than pick one of the two readings.
                if (localName == "on-empty" && HasContentAfter(child, skip))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0010,
                        "xsl:on-empty has to be the last instruction of its sequence constructor, so that "
                        + "what it asks about is all of the rest of it.");
                }
            }

            if (segments is null)
            {
                return instructions.ToArray();
            }

            if (instructions.Count > 0)
            {
                segments.Add(new ConditionalSegment(instructions.ToArray(), null));
            }

            return new Instruction[] { new ConditionalSequenceInstruction(segments.ToArray()) };
        }

        /// <summary>Whether text is whitespace and nothing else.</summary>
        /// <param name="text">The text.</param>
        private static bool IsBlank(string text) => text.AsSpan().Trim(" \t\r\n".AsSpan()).IsEmpty;

        /// <summary>
        /// Whether the whole run of text a node belongs to is whitespace, a comment or a processing
        /// instruction between two pieces of it not dividing them.
        /// </summary>
        /// <remarks>
        /// A stylesheet is prepared by removing its comments and processing instructions and only then
        /// stripping whitespace-only text (XSLT 3.0 §4.2). A tree holds no two adjacent text nodes, so by
        /// the time the stripping happens what stood on either side of a removed comment is one text node,
        /// and whitespace beside something that is not whitespace is kept along with it. So
        /// <c>&lt;e&gt;   h&lt;!--c--&gt;   &lt;/e&gt;</c> keeps all of its spaces, and the same written
        /// with no comment in it keeps them too — which is the point: a comment changes nothing.
        /// </remarks>
        /// <param name="node">The text node being considered.</param>
        private bool WholeRunIsWhitespace(int node)
        {
            int parent = m_tree.ParentOf(node);

            if (parent < 0)
            {
                return true;
            }

            bool blank = true;
            bool reached = false;

            for (int child = m_tree.FirstChildOf(parent); child >= 0; child = m_tree.NextSiblingOf(child))
            {
                if (m_tree.KindOf(child) is NodeKind.Text or NodeKind.Comment
                    or NodeKind.ProcessingInstruction)
                {
                    blank = blank
                        && (m_tree.KindOf(child) != NodeKind.Text || IsBlank(m_tree.StringValueOf(child)));
                    reached = reached || child == node;
                    continue;
                }

                // An element divides one run from the next, being a node the preparation leaves standing.
                if (reached)
                {
                    return blank;
                }

                blank = true;
            }

            return blank;
        }

        /// <summary>
        /// The run of text a node belongs to, and whether the node begins it.
        /// </summary>
        /// <remarks>
        /// The same reading of §4.2 that <see cref="WholeRunIsWhitespace"/> makes, and for the same reason:
        /// the comments and processing instructions are removed before the stylesheet is read, so a run of
        /// text with one of them in the middle is a single text node by the time anything looks at it.
        /// Stripping only has to know whether that run is blank. A text value template has to have the text
        /// itself, because the brace that opens an expression and the brace that closes it may stand in
        /// different pieces of the run — the suite writes <c>{str&lt;!--c--&gt;ing($p)}</c>.
        /// </remarks>
        /// <param name="node">The text node being compiled.</param>
        private (bool First, string Text) RunOfText(int node)
        {
            int parent = m_tree.ParentOf(node);

            if (parent < 0)
            {
                return (true, m_tree.StringValueOf(node));
            }

            System.Text.StringBuilder run = new System.Text.StringBuilder();
            int start = -1;
            bool holds = false;

            for (int child = m_tree.FirstChildOf(parent); child >= 0; child = m_tree.NextSiblingOf(child))
            {
                NodeKind kind = m_tree.KindOf(child);

                if (kind == NodeKind.Text)
                {
                    start = start < 0 ? child : start;
                    holds = holds || child == node;
                    run.Append(m_tree.StringValueOf(child));
                    continue;
                }

                if (kind is NodeKind.Comment or NodeKind.ProcessingInstruction)
                {
                    continue;
                }

                // An element divides one run from the next, being a node the preparation leaves standing.
                if (holds)
                {
                    break;
                }

                run.Clear();
                start = -1;
            }

            return (start == node, run.ToString());
        }

        private void CompileNode(int node, List<Instruction> output)
        {
            switch (m_tree.KindOf(node))
            {
                case NodeKind.Text:
                {
                    string text = m_tree.StringValueOf(node);

                    // Whitespace-only text in a stylesheet is stripped, unless xml:space="preserve" is in force
                    // where it stands; xsl:text preserves it too, and that is handled where xsl:text itself
                    // is compiled. What counts as one text node is settled first: see WholeRunIsWhitespace.
                    if (IsBlank(text) && WholeRunIsWhitespace(node) && !SpaceIsPreserved(node))
                    {
                        return;
                    }

                    // Where braces have to be found in the text, the run is what they are found in: the
                    // first piece of a run compiles the whole of it and the rest compile nothing. Only a
                    // text value template can tell the difference — pieces written separately and a run
                    // written whole produce the same text — so only it pays for the scan.
                    if (ExpandsText(node))
                    {
                        (bool first, string run) = RunOfText(node);

                        if (!first)
                        {
                            return;
                        }

                        text = run;
                    }

                    output.Add(CompileText(node, text, false));
                    return;
                }

                case NodeKind.Element:
                    int before = output.Count;

                    if (IsXsltElement(node, out string localName))
                    {
                        CompileXsltInstruction(node, localName, output);
                    }
                    else if (IsExtensionElement(node))
                    {
                        // A namespace the specifications have already given a meaning cannot be handed to an
                        // extension: an element in one is what this or another specification says it is, and
                        // an xsl:fallback does not make it otherwise. Static, and before the fallback is
                        // even looked for.
                        if (Array.IndexOf(XsltElements.Reserved, NamespaceIn(m_tree, node)) >= 0)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE0800,
                                $"'{QualifiedNameOf(node)}' is named in '{NamespaceIn(m_tree, node)}', which "
                                + "the specifications reserve, and is written where an extension instruction "
                                + "would go. An extension namespace has to be one nothing else has claimed.");
                        }

                        // An extension element is an instruction, not part of the result, so it cannot simply
                        // be copied out. This engine implements none, so every one of them falls back.
                        output.Add(CompileFallback(
                            node,
                            $"'{QualifiedNameOf(node)}' is an extension element, and this engine implements no "
                            + "extension elements. Give it an xsl:fallback child to say what to do instead.",
                            XsltErrorCode.XTDE1450));
                    }
                    else
                    {
                        output.Add(CompileLiteralElement(node));
                    }

                    Locate(output, before, node);
                    return;

                default:
                    // Comments and processing instructions in a stylesheet are not copied to the result.
                    return;
            }
        }

        private Instruction CompileLiteralElement(int element)
        {
            m_scopeElement = element;

            int nameCode = m_tree.NameCodeOf(element);
            int fingerprint = m_tree.FingerprintOf(element);
            NameTable names = m_tree.NameTable;

            int attributeCount = m_tree.AttributeCountOf(element);
            List<LiteralAttribute> attributes = new List<LiteralAttribute>(attributeCount);

            for (int i = 0; i < attributeCount; i++)
            {
                int attribute = m_tree.AttributeAt(element, i);
                int attributeNameCode = m_tree.NameCodeOf(attribute);
                int attributeFingerprint = m_tree.FingerprintOf(attribute);

                // Attributes in the XSLT namespace on a literal element are instructions to the processor,
                // not part of the result.
                if (names.GetNamespaceUri(attributeFingerprint) == XsltNamespace)
                {
                    string directive = names.GetLocalName(attributeFingerprint);

                    // A directive 3.0 added is no directive at all below 3.0, the same rule the XSLT
                    // elements' own attributes follow.
                    if (!IsForwardsCompatible(element)
                        && (Array.IndexOf(XsltElements.OnResultElement, directive) < 0
                            || (XsltElements.AddedIn30(directive) && !Implements30)))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0805,
                            $"'xsl:{directive}' is not a directive XSLT defines for a literal result "
                            + "element. An attribute in the XSLT namespace addresses the processor, so it "
                            + "cannot be carried into the result either.");
                    }

                    continue;
                }

                // An attribute written with no prefix is in no namespace, and there is nothing there to
                // alias. An element written with no prefix is in the default namespace, which is a
                // namespace an alias may name, so the two are not the same case: aliasing the default
                // namespace moves <stylesheet version="1.0"/> to xsl:stylesheet and leaves the version
                // attribute where it was written, which is what a stylesheet writing out a stylesheet
                // means (W3C bug 30397).
                string writtenPrefix = names.GetPrefix(attributeNameCode);
                string writtenUri = names.GetNamespaceUri(attributeFingerprint);

                (string attributePrefix, string attributeUri) = writtenUri.Length == 0
                    ? (writtenPrefix, writtenUri)
                    : ApplyNamespaceAlias(writtenPrefix, writtenUri);

                attributes.Add(new LiteralAttribute(
                    attributePrefix,
                    attributeUri,
                    names.GetLocalName(attributeFingerprint),
                    AttributeValueTemplate.Parse(m_tree.StringValueOf(attribute), this, m_backend)));
            }

            (string Prefix, string Uri)[] namespaces = CollectResultNamespaces(element);

            // On a literal result element the directives are themselves in the XSLT namespace, which is what
            // keeps an ordinary type attribute — <input type="text"/> — part of the result rather than a
            // request to validate against a schema. A schema-aware processor reads xsl:validation and
            // xsl:type there and validates as WithValidation says.
            RejectSchemaValidation(element, xslt: true);
            ExpandedName[] attributeSets = ReadAttributeSetNames(element);

            Instruction[] body = CompileSequence(element);
            m_scopeElement = element;

            // xsl:namespace-alias applies to the element's own name as well as to the namespaces it carries.
            (string elementPrefix, string elementUri) = ApplyNamespaceAlias(
                names.GetPrefix(nameCode), names.GetNamespaceUri(fingerprint));

            Instruction literal = new LiteralElementInstruction(
                elementPrefix,
                elementUri,
                names.GetLocalName(fingerprint),
                namespaces,
                attributes.ToArray(),
                body,
                attributeSets,
                ReadInheritNamespaces(element, xslt: true));

            return WithValidation(element, literal, ValidationShape.Element, literal: true);
        }

        /// <summary>
        /// Collects the namespaces that a literal result element carries into the output.
        /// </summary>
        /// <remarks>
        /// XSLT copies every namespace node in scope on the element, which is why a stylesheet that merely
        /// declares a prefix in order to write patterns still emits that declaration in its result. The XSLT
        /// namespace itself is always excluded, and <c>exclude-result-prefixes</c> removes others.
        /// </remarks>
        private (string Prefix, string Uri)[] CollectResultNamespaces(int element)
        {
            HashSet<string> excluded = CollectExcludedNamespaces(element);
            List<(string Prefix, string Uri)> namespaces = new();

            foreach ((string prefix, string uri) in m_tree.InScopeNamespacesOf(element))
            {
                // §11.1.4: a namespace node for an alias's literal namespace is not copied, and one for its
                // target namespace is copied whether or not that namespace is excluded — which is what
                // leaves the element with a binding for the result prefix and none for the stand-in.
                if (IsAliasSource(uri))
                {
                    continue;
                }

                // The XSLT namespace is excluded like any other, and an alias whose target it is copies it
                // like any other: that is how a stylesheet writes a stylesheet.
                if (!IsAliasTarget(uri) && (uri == XsltNamespace || excluded.Contains(uri)))
                {
                    continue;
                }

                namespaces.Add((prefix, uri));
            }

            return namespaces.ToArray();
        }

        /// <summary>
        /// Gathers the namespaces kept out of the result by this element or any ancestor, by URI.
        /// </summary>
        /// <remarks>
        /// A prefix in <c>exclude-result-prefixes</c> designates the namespace it is bound to where the
        /// attribute is written (§11.1.4), not the prefix itself: an ancestor excluding a URI under one
        /// prefix excludes it under any prefix a descendant binds it to, and a descendant that rebinds the
        /// same prefix to another URI keeps that one. <c>extension-element-prefixes</c> counts alongside it:
        /// a namespace declared so that the stylesheet can name extension elements exists for the
        /// processor's benefit, and carrying it into the result would leak a detail of how the stylesheet
        /// was written.
        /// </remarks>
        private HashSet<string> CollectExcludedNamespaces(int element)
        {
            HashSet<string> excluded = new HashSet<string>(StringComparer.Ordinal);

            foreach (string attributeName in new[] { "exclude-result-prefixes", "extension-element-prefixes" })
            {
                for (int current = element; current >= 0; current = m_tree.ParentOf(current))
                {
                    if (m_tree.KindOf(current) != NodeKind.Element)
                    {
                        continue;
                    }

                    string? declaration = GetAttribute(current, attributeName)
                        ?? GetXsltAttribute(current, attributeName);

                    if (declaration is null)
                    {
                        continue;
                    }

                    foreach (string token in declaration.Split(
                        (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (token == "#all" && attributeName == "exclude-result-prefixes")
                        {
                            foreach ((_, string uri) in m_tree.InScopeNamespacesOf(current))
                            {
                                excluded.Add(uri);
                            }

                            continue;
                        }

                        RequireBoundPrefix(current, attributeName, token);
                        excluded.Add(m_tree.ResolvePrefix(current, token == "#default" ? string.Empty : token)!);
                    }
                }
            }

            return excluded;
        }

        /// <summary>
        /// Gathers the prefixes named by one whitespace-separated attribute on this element or any ancestor.
        /// The token <c>#default</c> refers to the default namespace.
        /// </summary>
        /// <remarks>
        /// The attribute is unprefixed on an XSLT element and lives in the XSLT namespace on a literal result
        /// element, so both spellings are read.
        /// </remarks>
        private HashSet<string> CollectPrefixTokens(int element, string attributeName)
        {
            HashSet<string> prefixes = new(StringComparer.Ordinal);

            for (int current = element; current >= 0; current = m_tree.ParentOf(current))
            {
                if (m_tree.KindOf(current) != NodeKind.Element)
                {
                    continue;
                }

                string? declaration = GetAttribute(current, attributeName)
                    ?? GetXsltAttribute(current, attributeName);

                if (declaration is null)
                {
                    continue;
                }

                foreach (string token in declaration.Split(
                    (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    // #all is every prefix in scope where the attribute was written, which XSLT 2.0 added
                    // so a stylesheet need not list the namespaces it happens to have declared. Only
                    // exclude-result-prefixes takes it.
                    if (token == "#all" && attributeName == "exclude-result-prefixes")
                    {
                        foreach ((string prefix, _) in m_tree.InScopeNamespacesOf(current))
                        {
                            prefixes.Add(prefix);
                        }

                        continue;
                    }

                    RequireBoundPrefix(current, attributeName, token);
                    prefixes.Add(token == "#default" ? string.Empty : token);
                }
            }

            return prefixes;
        }

        /// <summary>
        /// Refuses a prefix that no declaration on the element carrying the attribute binds.
        /// </summary>
        /// <remarks>
        /// Both attributes name prefixes to be read against the element they are written on, and a prefix
        /// nothing binds names no namespace — so the attribute says nothing and the stylesheet meant it to
        /// say something. The commonest way to write one is with commas, which the grammar does not allow:
        /// <c>"one, two"</c> is two prefixes called <c>one,</c> and <c>two</c>, neither of them bound.
        /// <para>
        /// Three codes for one complaint, as usual: <c>XTSE0808</c> for a prefix on
        /// <c>exclude-result-prefixes</c>, <c>XTSE0809</c> for <c>#default</c> there with no default
        /// namespace in scope, and <c>XTSE1430</c> for either on <c>extension-element-prefixes</c>.
        /// </para>
        /// </remarks>
        /// <param name="element">The element carrying the attribute.</param>
        /// <param name="attributeName">Which attribute it is.</param>
        /// <param name="token">One token of its value.</param>
        private void RequireBoundPrefix(int element, string attributeName, string token)
        {
            bool extension = attributeName == "extension-element-prefixes";
            bool isDefault = token == "#default";

            if (m_tree.ResolvePrefix(element, isDefault ? string.Empty : token) is { Length: > 0 })
            {
                return;
            }

            XsltErrorCode code = extension
                ? XsltErrorCode.XTSE1430
                : isDefault ? XsltErrorCode.XTSE0809 : XsltErrorCode.XTSE0808;

            throw XsltErrors.Error(
                code,
                isDefault
                    ? $"'{attributeName}' says #default where '{QualifiedNameOf(element)}' declares no "
                        + "default namespace, so there is no namespace for it to name."
                    : $"'{attributeName}' names the prefix '{token}', which no namespace declaration on "
                        + $"'{QualifiedNameOf(element)}' binds. The prefixes are separated by spaces, not "
                        + "by commas.");
        }

        /// <summary>
        /// Returns whether an element outside the XSLT namespace is an extension element rather than a literal
        /// result element.
        /// </summary>
        /// <remarks>
        /// The two are told apart solely by <c>extension-element-prefixes</c>. Nothing about the element itself
        /// distinguishes them, which is deliberate: the same markup is result content on a processor that was
        /// not told otherwise.
        /// </remarks>
        private bool IsExtensionElement(int element)
        {
            HashSet<string> prefixes = CollectPrefixTokens(element, "extension-element-prefixes");
            if (prefixes.Count == 0)
            {
                return false;
            }

            string elementUri = m_tree.NameTable.GetNamespaceUri(m_tree.FingerprintOf(element));

            foreach (string prefix in prefixes)
            {
                if (m_tree.ResolvePrefix(element, prefix) == elementUri)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The base URI in force where an expression is written: the <c>xml:base</c> declarations above it,
        /// resolved outwards and finally against the stylesheet's own URI.
        /// </summary>
        /// <remarks>
        /// This is what a relative reference in the stylesheet resolves against, and what
        /// <c>fn:static-base-uri()</c> reports. Static, so it is settled here — a stylesheet assembled from
        /// several places says with <c>xml:base</c> where each part came from, and a reference written in
        /// one of them means what it meant there.
        /// </remarks>
        /// <param name="element">The element the expression is written on.</param>
        private string? StaticBaseUri(int element)
        {
            List<string>? declared = null;

            string? entityBase = null;

            for (int current = element; current >= 0; current = m_tree.ParentOf(current))
            {
                if (m_tree.KindOf(current) == NodeKind.Element
                    && GetXmlAttribute(current, "base") is string written)
                {
                    (declared ??= new List<string>()).Add(written);
                }

                // An element parsed out of an external entity has the entity's URI for its base, and
                // nothing above it in the module says otherwise.
                if (m_tree.EntityBaseOf(current) is string fromEntity)
                {
                    entityBase = fromEntity;
                    break;
                }
            }

            // A module's base URI is where it was read from: an included module in another directory
            // resolves what it names relative to itself, as the specification has it.
            string? resolved = entityBase
                ?? (m_moduleUris.TryGetValue(m_tree, out string? moduleUri) ? moduleUri : m_options.BaseUri);

            for (int i = (declared?.Count ?? 0) - 1; i >= 0; i--)
            {
                string written = declared![i];

                if (!UriReference.TryParse(written, out UriReference reference))
                {
                    return null;
                }

                if (reference.Scheme is not null)
                {
                    resolved = UriReference.Resolve(reference, reference);
                }
                else if (resolved is not null
                    && UriReference.TryParse(resolved, out UriReference root) && root.Scheme is not null)
                {
                    resolved = UriReference.Resolve(reference, root);
                }
                else
                {
                    resolved = written;
                }
            }

            return resolved;
        }

        /// <summary>The value of an attribute in the <c>xml</c> namespace, if the element has one.</summary>
        /// <param name="element">The element to look on.</param>
        /// <param name="localName">The attribute's local name.</param>
        private string? GetXmlAttribute(int element, string localName)
        {
            int count = m_tree.AttributeCountOf(element);

            for (int i = 0; i < count; i++)
            {
                int attribute = m_tree.AttributeAt(element, i);
                int fingerprint = m_tree.FingerprintOf(attribute);

                if (m_tree.NameTable.GetLocalName(fingerprint) == localName
                    && m_tree.NameTable.GetNamespaceUri(fingerprint) == XdmTree.XmlNamespaceUri)
                {
                    return m_tree.StringValueOf(attribute);
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public string DefaultElementNamespace => DefaultElementNamespaceAt(m_scopeElement);

        /// <summary>
        /// The namespace an unprefixed name test is in where it is written, which is what
        /// <c>xpath-default-namespace</c> declares.
        /// </summary>
        /// <param name="element">The element the name test is written on.</param>
        private string DefaultElementNamespaceAt(int element)
        {
            // The nearest declaration wins, and an empty value puts unprefixed names back in no
            // namespace — which is how a stylesheet turns the attribute off for one subtree.
            for (int current = element; current >= 0; current = m_tree.ParentOf(current))
            {
                if (m_tree.KindOf(current) != NodeKind.Element)
                {
                    continue;
                }

                string? declared = (IsXsltElement(current, out _)
                        ? GetAttribute(current, "xpath-default-namespace")
                        : null)
                    ?? GetXsltAttribute(current, "xpath-default-namespace");

                if (declared is not null)
                {
                    return declared;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Returns the XSLT version in force for an element: the nearest <c>version</c> or <c>xsl:version</c>
        /// on the element itself or any ancestor.
        /// </summary>
        /// <remarks>
        /// Only <c>xsl:version</c> counts on a literal result element — an unprefixed <c>version</c> there is
        /// part of the result, not a message to the processor. On an XSLT element it is the other way round.
        /// A stylesheet that names no version at all is read as XSLT 1.0.
        /// </remarks>
        private XsltVersion VersionOf(int element)
        {
            // A shadow attribute exists only at 3.0, so while the one computing the version is being read
            // the version is the processor's own: asking the element would be asking the expression to
            // tell what it needs to know before it can be parsed.
            if (m_shadowVersionDepth > 0)
            {
                return m_options.Version;
            }

            for (int current = element; current >= 0; current = m_tree.ParentOf(current))
            {
                if (m_tree.KindOf(current) != NodeKind.Element)
                {
                    continue;
                }

                // On xsl:output, version is the version of the output method — HTML 4.01, XML 1.1 — and says
                // nothing about which XSLT this is. Everywhere else an unprefixed version does, and on a
                // literal result element the prefixed one does.
                string? version =
                    (IsXsltElement(current, out string localName) && localName != "output"
                        ? GetAttribute(current, "version")
                        : null)
                    ?? GetXsltAttribute(current, "version");

                if (version is not null)
                {
                    // Checked here rather than with the rest of the attributes, because what it settles is
                    // whether the rest are checked at all: read as naming a later XSLT, a value that is not a
                    // version at all would turn every other check off.
                    RequireDecimal(version);
                    return XsltVersion.Parse(version);
                }
            }

            return XsltVersion.V10;
        }

        /// <summary>
        /// Whether text written at an element is a text value template.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XSLT 3.0 §5.7.2. <c>expand-text</c> is inherited down the stylesheet tree exactly as
        /// <c>version</c> and <c>xml:space</c> are, and may be turned back off at any depth — so the nearest
        /// ancestor that says anything is the one that decides, and a stylesheet that says nothing means no.
        /// The prefixed form is what a literal result element carries, the unprefixed one what an XSLT
        /// element does, and this reads either wherever it stands.
        /// </para>
        /// <para>
        /// The values are the four the specification allows and it accepts them with space around, which is
        /// not laxity: the suite writes <c>expand-text=" true "</c>, and an attribute in a stylesheet is
        /// whitespace-normalized by the XML parser rather than trimmed.
        /// </para>
        /// </remarks>
        private bool ExpandsText(int element)
        {
            for (int current = element; current >= 0; current = m_tree.ParentOf(current))
            {
                if (m_tree.KindOf(current) != NodeKind.Element)
                {
                    continue;
                }

                string? said = (IsXsltElement(current, out _) ? GetAttribute(current, "expand-text") : null)
                    ?? GetXsltAttribute(current, "expand-text");

                // Below 3.0 there is no such attribute, so text with a brace in it is text with a brace in
                // it — which is what every 1.0 and 2.0 stylesheet ever written assumed.
                if (said is null || !Implements30)
                {
                    continue;
                }

                return said.Trim() switch
                {
                    "yes" or "true" or "1" => true,
                    "no" or "false" or "0" => false,
                    _ => throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"The expand-text attribute of {QualifiedNameOf(current)} is '{said}', which is "
                        + "neither 'yes' nor 'no'."),
                };
            }

            return false;
        }

        /// <summary>
        /// The mode in force where a <c>mode</c> attribute was not written, which XSLT 3.0 lets a stylesheet
        /// choose.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It reads the nearest <c>default-mode</c> in scope, on an XSLT element or as <c>xsl:default-mode</c>
        /// on a literal result element, and answers the unnamed mode where there is none — which is what every
        /// stylesheet before 3.0 meant by saying nothing.
        /// </para>
        /// <para>
        /// It settles two questions that look separate and are the same one. On <c>xsl:apply-templates</c> it
        /// says which mode to apply <em>in</em>; on <c>xsl:template</c> it says which mode the rule
        /// <em>belongs to</em>. A stylesheet writing whole groups of rules in a named mode therefore says so
        /// once, at the top, instead of on every rule and every call — which is the point of it, since the
        /// omission of one <c>mode="x"</c> in such a stylesheet is silent and puts a rule somewhere nothing
        /// reaches.
        /// </para>
        /// </remarks>
        private int DefaultModeIn(int element)
        {
            for (int current = element; current >= 0; current = m_tree.ParentOf(current))
            {
                if (m_tree.KindOf(current) != NodeKind.Element)
                {
                    continue;
                }

                string? said = (IsXsltElement(current, out _) ? GetAttribute(current, "default-mode") : null)
                    ?? GetXsltAttribute(current, "default-mode");

                if (said is null || !Implements30)
                {
                    continue;
                }

                said = said.Trim();

                // The value check has to be here rather than in the element table: the table is not consulted
                // under forwards-compatible processing, and this attribute is read there too.
                if (said == "#unnamed")
                {
                    return CompiledStylesheet.DefaultMode;
                }

                if (!IsLexicalQName(said))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0550,
                        $"The default-mode attribute of {QualifiedNameOf(current)} is '{said}', which is "
                        + "neither a mode name nor '#unnamed'.");
                }

                // Resolved against the element that wrote it, so a prefix means what it means there rather
                // than what it happens to mean wherever the default is being read from.
                return ResolveMode(current, said);
            }

            return CompiledStylesheet.DefaultMode;
        }

        /// <summary>
        /// Compiles text written in a sequence constructor, as a text value template where one is in effect.
        /// </summary>
        /// <remarks>
        /// A text value template is an attribute value template that happens to be written as content, down
        /// to the doubled brace meaning a literal one — so it is the same parser, and a template with no
        /// braces in it comes back constant and costs nothing.
        /// </remarks>
        private Instruction CompileText(int node, string text, bool disableEscaping)
        {
            if (!ExpandsText(node))
            {
                return new TextInstruction(text, disableEscaping);
            }

            m_scopeElement = m_tree.ParentOf(node);
            AttributeValueTemplate template = AttributeValueTemplate.Parse(text, this, m_backend);

            return template.ConstantValue is string constant
                ? new TextInstruction(constant, disableEscaping)
                : new TextValueTemplateInstruction(template, disableEscaping);
        }

        /// <summary>
        /// Returns whether forwards-compatible processing is in effect for an element — that is, whether the
        /// version in scope is later than the one this engine implements.
        /// </summary>
        /// <remarks>
        /// It is what lets a stylesheet be written for XSLT 2.0 and still run here as far as it can: an
        /// instruction this engine has never heard of becomes something to fall back from at run time rather
        /// than a reason to reject the stylesheet outright.
        /// </remarks>
        private bool IsForwardsCompatible(int element)
        {
            return VersionOf(element) > m_options.Version;
        }

        /// <summary>
        /// Checks an XSLT element against what the specification allows: where it may stand, what it must
        /// carry, and what it may carry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Nothing here changes what a correct stylesheet does. What it changes is what an incorrect one
        /// hears: a misspelled attribute used to be read as absent and the stylesheet compiled around the
        /// hole, so <c>&lt;xsl:sort ordr="descending"/&gt;</c> sorted ascending and said nothing. The
        /// specification requires it to be refused, and gives three codes for the three ways of being wrong.
        /// </para>
        /// <para>
        /// Skipped entirely under forwards-compatible processing. There the stylesheet was written for a
        /// later version of XSLT, where the element or the attribute this engine has never heard of may be
        /// perfectly ordinary — refusing it would be refusing the stylesheet for being newer.
        /// </para>
        /// </remarks>
        /// <param name="element">The element to check.</param>
        /// <param name="localName">Its local name, already known to be in the XSLT namespace.</param>
        /// <param name="placement">Where it was found.</param>
        private void ValidateXsltElement(int element, string localName, XsltPlacement placement)
        {
            if (IsForwardsCompatible(element))
            {
                return;
            }

            XsltElement? shape = XsltElements.Find(localName);

            if (shape is null || !IsAvailable(shape))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0010,
                    $"'{QualifiedNameOf(element)}' is not an element the XSLT specification defines"
                    + (shape is null ? "." : $" in version {m_options.Version}, which is what this processor reads."));
            }

            if ((shape.Placement & placement) == 0 && !StandsInAnAllowedParent(element, shape))
            {
                throw XsltErrors.Error(
                    shape.Misplaced ?? XsltErrorCode.XTSE0010,
                    $"'{QualifiedNameOf(element)}' cannot be written here."
                    + (shape.Parents.Length == 0
                        ? string.Empty
                        : $" It belongs inside {string.Join(" or ", shape.Parents.Select(name => $"xsl:{name}"))}."));
            }

            // Before the required attributes, and that ordering is the point. The specification says a
            // non-schema-aware processor must signal this, so a stylesheet asking for validation hears that
            // rather than hearing about a second thing wrong with the same element — which is what
            // 'xsl:element type="xs:untyped"', missing its name, would otherwise be told about first.
            if (Array.IndexOf(shape.Optional, "type") >= 0 || Array.IndexOf(shape.Optional, "validation") >= 0)
            {
                RejectSchemaValidation(element);
            }

            RejectSchemaDefaultValidation(element);

            foreach (string required in shape.Required)
            {
                if (GetAttribute(element, required) is null)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0010,
                        $"'{QualifiedNameOf(element)}' requires a '{required}' attribute.");
                }
            }

            ValidateXsltAttributes(element, shape);
            ValidateXsltContent(element, shape);
            ValidateXsltElementRules(element, localName);
        }

        /// <summary>
        /// Checks the rules the specification states for one element rather than for elements in general.
        /// </summary>
        /// <remarks>
        /// Each of these is a stylesheet saying two things where only one can be acted on — a sort key
        /// written twice, a key with no way of computing its value, a template that can neither be matched
        /// nor called. The specification gives each its own code, which is the whole reason they are stated
        /// separately rather than folded into the content model.
        /// </remarks>
        private void ValidateXsltElementRules(int element, string localName)
        {
            switch (localName)
            {
                case "template":
                {
                    bool matches = GetAttribute(element, "match") is not null;

                    if (!matches && GetAttribute(element, "name") is null)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0500,
                            "xsl:template must have a match or a name, or both. With neither there is no way "
                            + "of reaching it.");
                    }

                    if (!matches && GetAttribute(element, "mode") is not null)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0500,
                            "xsl:template has a mode and no match. A mode says which rules apply to a node, "
                            + "and this template is not a rule.");
                    }

                    if (!matches && GetAttribute(element, "priority") is not null)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0500,
                            "xsl:template has a priority and no match. A priority decides between rules that "
                            + "match one node, and this template is not a rule.");
                    }

                    if (GetAttribute(element, "name") is null && GetAttribute(element, "visibility") is not null)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0500,
                            "xsl:template has a visibility and no name. Visibility is a named template's, "
                            + "which is the component another package could see; a rule is reached through "
                            + "its mode.");
                    }

                    break;
                }

                case "sort":
                {
                    if (GetAttribute(element, "select") is not null && HasContent(element))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1015,
                            "xsl:sort has both a select attribute and content. The sort key comes from one "
                            + "or the other.");
                    }

                    if (GetAttribute(element, "stable") is not null && !IsFirstSort(element))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1017,
                            "stable is written on an xsl:sort that is not the first. Stability is a property "
                            + "of the whole sort, so it is stated once, on the first key.");
                    }

                    break;
                }

                case "perform-sort" when GetAttribute(element, "select") is not null:
                {
                    for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
                    {
                        bool allowed = m_tree.KindOf(child) switch
                        {
                            NodeKind.Element => IsXsltElement(child, out string name)
                                && name is "sort" or "fallback",
                            NodeKind.Text => m_tree.StringValueOf(child).AsSpan().IsWhiteSpace(),
                            _ => true,
                        };

                        if (!allowed)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE1040,
                                "xsl:perform-sort has both a select attribute and content of its own. What "
                                + "it sorts comes from one or the other.");
                        }
                    }

                    break;
                }

                case "key":
                {
                    // A declaration written inside the key is misplaced before it is content: the
                    // specification's own code for it, rather than a complaint about the content it makes.
                    for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
                    {
                        if (m_tree.KindOf(child) == NodeKind.Element
                            && IsXsltElement(child, out string childName)
                            && XsltElements.Find(childName) is { Placement: XsltPlacement.Declaration })
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE0010,
                                $"'{QualifiedNameOf(child)}' is a declaration, and cannot be written inside "
                                + "xsl:key: the content of a key is a sequence constructor.");
                        }
                    }

                    bool uses = GetAttribute(element, "use") is not null;

                    if (uses && HasContent(element))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1205,
                            "xsl:key has both a use attribute and content. The key value comes from one or "
                            + "the other.");
                    }

                    if (!uses && !HasContent(element))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1205,
                            "xsl:key has neither a use attribute nor content, so there is nothing to compute "
                            + "the key value from.");
                    }

                    break;
                }

                case "analyze-string":
                {
                    ValidateAnalyzeStringBranches(element);
                    break;
                }

                case "param":
                {
                    RequireNoEarlierParam(element);

                    if (m_tree.ParentOf(element) is int parent
                        && parent >= 0
                        && IsXsltElement(parent, out string parentName)
                        && parentName == "function"
                        && (GetAttribute(element, "select") is not null || HasContent(element)))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0760,
                            "an xsl:param of an xsl:function has a default value. Every argument of a "
                            + "stylesheet function is supplied at the call, so a default could never be used.");
                    }

                    break;
                }

                case "apply-templates" when GetAttribute(element, "mode") is string mode
                    && mode.Trim() is not ("#current" or "#default")
                    && !(mode.Trim() == "#unnamed" && Implements30)
                    && !IsLexicalQName(mode.Trim()):
                {
                    // mode is not an attribute value template, so curly brackets in it are eleven characters
                    // of a name rather than an expression to evaluate. Read as written, this names no mode.
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"'{mode}' is not a mode. The mode of xsl:apply-templates is a QName written out, or "
                        + "'#current', or '#default'"
                        + (Implements30 ? ", or '#unnamed'" : string.Empty)
                        + " — not an expression and not an attribute value template.");
                }

                case "for-each-group" when GetAttribute(element, "collation") is not null
                    && GetAttribute(element, "group-by") is null
                    && GetAttribute(element, "group-adjacent") is null:
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE1090,
                        "xsl:for-each-group has a collation and groups by a pattern. A collation compares "
                        + "grouping keys, and grouping by a pattern computes none.");
                }
            }
        }

        /// <summary>Whether an <c>xsl:sort</c> is the first of the sorts it stands among.</summary>
        private bool IsFirstSort(int element)
        {
            for (int sibling = m_tree.FirstChildOf(m_tree.ParentOf(element));
                sibling >= 0;
                sibling = m_tree.NextSiblingOf(sibling))
            {
                if (sibling == element)
                {
                    return true;
                }

                if (m_tree.KindOf(sibling) == NodeKind.Element
                    && IsXsltElement(sibling, out string name)
                    && name == "sort")
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Refuses a second <c>xsl:param</c> of a name a preceding sibling already declared.</summary>
        private void RequireNoEarlierParam(int element)
        {
            string? name = GetAttribute(element, "name");
            if (name is null)
            {
                return;
            }

            ExpandedName declared = ResolveQualifiedName(element, name);

            for (int sibling = m_tree.FirstChildOf(m_tree.ParentOf(element));
                sibling >= 0 && sibling != element;
                sibling = m_tree.NextSiblingOf(sibling))
            {
                if (m_tree.KindOf(sibling) != NodeKind.Element
                    || !IsXsltElement(sibling, out string siblingName)
                    || siblingName != "param"
                    || GetAttribute(sibling, "name") is not string earlier)
                {
                    continue;
                }

                if (ResolveQualifiedName(sibling, earlier) == declared)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0580,
                        $"'{name}' is declared twice as a parameter of "
                        + $"'{QualifiedNameOf(m_tree.ParentOf(element))}'. The second declaration could never "
                        + "be reached.");
                }
            }
        }

        /// <summary>
        /// Checks that <c>xsl:analyze-string</c> holds one branch or both, the matching one first.
        /// </summary>
        /// <remarks>
        /// The content takes one of three forms (§17.1): an xsl:matching-substring, an
        /// xsl:non-matching-substring, or the two in that order, each followed by nothing but xsl:fallback.
        /// Neither branch may appear twice. Having neither is the one mistake with a code of its own,
        /// <c>XTSE1130</c>; a branch out of place is <c>XTSE0010</c>, and so is anything after a fallback,
        /// which the element's shape says.
        /// </remarks>
        private void ValidateAnalyzeStringBranches(int element)
        {
            bool matching = false;
            bool nonMatching = false;

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element || !IsXsltElement(child, out string name))
                {
                    continue;
                }

                bool misplaced = name switch
                {
                    "matching-substring" => matching || nonMatching,
                    "non-matching-substring" => nonMatching,
                    _ => false,
                };

                if (misplaced)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0010,
                        $"'{QualifiedNameOf(child)}' cannot stand here: xsl:analyze-string holds an "
                        + "xsl:matching-substring, an xsl:non-matching-substring, or the two in that order, "
                        + "each once.");
                }

                matching |= name == "matching-substring";
                nonMatching |= name == "non-matching-substring";
            }

            if (!matching && !nonMatching)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE1130,
                    "xsl:analyze-string has neither an xsl:matching-substring nor an "
                    + "xsl:non-matching-substring, so it produces nothing whatever the regex does.");
            }
        }

        /// <summary>
        /// Stamps the instructions compiled from one element with where that element stands, for an
        /// <c>xsl:catch</c> to report.
        /// </summary>
        private void Locate(List<Instruction> output, int from, int element)
        {
            int line = m_tree.LineOf(element);

            if (line == 0 || from >= output.Count)
            {
                return;
            }

            SourceLocation location = new SourceLocation(ModuleUriOf(m_tree), line, m_tree.ColumnOf(element));

            for (int i = from; i < output.Count; i++)
            {
                output[i].Location ??= location;
            }
        }

        /// <summary>The URI a module was read from, or the stylesheet's base URI for the principal one.</summary>
        private string? ModuleUriOf(XdmTree module)
        {
            return m_moduleUris.TryGetValue(module, out string? uri) ? uri : m_options.BaseUri;
        }

        private bool HasChild(int element, string localName)
        {
            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string name)
                    && name == localName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks what an XSLT element holds against what it is allowed to hold.
        /// </summary>
        /// <remarks>
        /// Two rules, and both catch a stylesheet that says something and is answered with silence. An
        /// element with a fixed content model — <c>xsl:choose</c>, <c>xsl:call-template</c> — used to have
        /// anything else inside it quietly discarded. And an <c>xsl:param</c> written after the first
        /// instruction of a template, or an <c>xsl:sort</c> after the first instruction of an
        /// <c>xsl:for-each</c>, was skipped where it stood: a parameter that was never declared, a sort that
        /// never happened.
        /// </remarks>
        private void ValidateXsltContent(int element, XsltElement shape)
        {
            if (shape.Children is null && shape.Leading.Length == 0)
            {
                return;
            }

            bool mustBeEmpty = shape.Children is { Length: 0 } && !shape.Text;
            bool afterContent = false;
            int trailing = -1;
            int leading = -1;

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                NodeKind kind = m_tree.KindOf(child);

                if (kind is NodeKind.Comment or NodeKind.ProcessingInstruction)
                {
                    continue;
                }

                if (kind == NodeKind.Text)
                {
                    // Whitespace between elements is layout, and says nothing about the content model —
                    // unless the stylesheet asked for it to be kept, which inside an element required to be
                    // empty is the specification's own example of content that may not be there.
                    if (m_tree.StringValueOf(child).AsSpan().IsWhiteSpace())
                    {
                        if (mustBeEmpty && SpaceIsPreserved(child))
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE0260,
                                $"'{QualifiedNameOf(element)}' must be empty, and holds whitespace that "
                                + "xml:space=\"preserve\" keeps.");
                        }

                        continue;
                    }

                    if (shape.Children is not null && !shape.Text)
                    {
                        throw XsltErrors.Error(
                            EmptyElementCode(shape),
                            $"'{QualifiedNameOf(element)}' cannot hold text.");
                    }

                    afterContent = true;
                    continue;
                }

                bool isXslt = IsXsltElement(child, out string childName);

                if (trailing >= 0)
                {
                    // What may follow a trailing element is another trailing one, where the model has
                    // several: xsl:fallback* after an xsl:merge-action. One the model has only one of,
                    // xsl:otherwise, is followed by nothing.
                    bool another = isXslt
                        && Array.IndexOf(shape.Trailing, childName) >= 0
                        && XsltElements.Find(childName) is not { Once: true };

                    if (!another)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0010,
                            $"'{QualifiedNameOf(child)}' comes after '{QualifiedNameOf(trailing)}' in "
                            + $"'{QualifiedNameOf(element)}'. Nothing may follow it.");
                    }
                }

                if (shape.Children is not null
                    && (!isXslt || Array.IndexOf(shape.Children, childName) < 0))
                {
                    throw XsltErrors.Error(
                        EmptyElementCode(shape),
                        $"'{QualifiedNameOf(child)}' cannot be written inside '{QualifiedNameOf(element)}', "
                        + (shape.Children.Length == 0
                            ? "which holds nothing."
                            : $"which holds only {string.Join(" and ", shape.Children.Select(name => $"xsl:{name}"))}."));
                }

                // An element that belongs to a particular parent is read by that parent rather than compiled
                // as an instruction, so it is checked here or nowhere: an xsl:sort inside an xsl:for-each
                // never reaches the instruction compiler at all.
                if (isXslt
                    && XsltElements.Find(childName) is XsltElement subordinate
                    && (subordinate.Placement & XsltPlacement.Subordinate) != 0
                    && (subordinate.Placement & XsltPlacement.Instruction) == 0)
                {
                    ValidateXsltElement(child, childName, XsltPlacement.Subordinate);
                }

                int place = isXslt ? Array.IndexOf(shape.Leading, childName) : -1;

                if (place >= 0)
                {
                    if (afterContent)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0010,
                            $"'{QualifiedNameOf(child)}' comes after content in "
                            + $"'{QualifiedNameOf(element)}'. It must be written before anything else, or it "
                            + "does nothing where it stands.");
                    }

                    // The leading elements come in the order the content model lists them, which is the
                    // order they are written in here, and the ones it allows only one of are allowed only
                    // one. Both are the same complaint: an element in a place its parent does not have for
                    // it. An xsl:param after an xsl:context-item is a parameter; before it, it is a
                    // parameter and a declaration read out of order, which the specification does not let a
                    // processor put right on the stylesheet's behalf.
                    if (place < leading)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0010,
                            $"'{QualifiedNameOf(child)}' comes after an xsl:{shape.Leading[leading]} in "
                            + $"'{QualifiedNameOf(element)}', and belongs before it.");
                    }

                    if (place == leading && XsltElements.Find(childName) is { Once: true })
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0010,
                            $"'{QualifiedNameOf(element)}' holds more than one {QualifiedNameOf(child)}, and "
                            + "takes at most one.");
                    }

                    leading = place;
                    continue;
                }

                if (isXslt && Array.IndexOf(shape.Trailing, childName) >= 0)
                {
                    trailing = child;
                }

                afterContent = true;
            }
        }

        /// <summary>
        /// Whether <c>xml:space="preserve"</c> is in force where a node stands.
        /// </summary>
        /// <remarks>
        /// The attribute is inherited and either value may override the other, so the answer is the nearest
        /// ancestor that states one. A stylesheet tree keeps all its whitespace, so this is what tells layout
        /// apart from text the stylesheet meant to keep.
        /// </remarks>
        private bool SpaceIsPreserved(int node)
        {
            for (int ancestor = m_tree.ParentOf(node); ancestor >= 0; ancestor = m_tree.ParentOf(ancestor))
            {
                if (m_tree.KindOf(ancestor) != NodeKind.Element)
                {
                    break;
                }

                if (GetNamespacedAttribute(ancestor, XdmTree.XmlNamespaceUri, "space") is string value)
                {
                    return value.Trim() == "preserve";
                }
            }

            return false;
        }

        /// <summary>
        /// The code for content an element may not hold.
        /// </summary>
        /// <remarks>
        /// An element that must be empty and is not gets <c>XTSE0260</c>, which the specification names for
        /// exactly that and for nothing else. An element with a content model that this content does not fit
        /// gets the general <c>XTSE0010</c>, there being no code for each of them.
        /// </remarks>
        private static XsltErrorCode EmptyElementCode(XsltElement shape)
        {
            return shape.Children is { Length: 0 } && !shape.Text
                ? XsltErrorCode.XTSE0260
                : XsltErrorCode.XTSE0010;
        }

        private bool StandsInAnAllowedParent(int element, XsltElement shape)
        {
            if (shape.Parents.Length == 0)
            {
                return false;
            }

            int parent = m_tree.ParentOf(element);

            return parent >= 0
                && IsXsltElement(parent, out string parentName)
                && Array.IndexOf(shape.Parents, parentName) >= 0;
        }

        /// <summary>
        /// Checks every attribute an XSLT element carries in no namespace or in the XSLT namespace.
        /// </summary>
        /// <remarks>
        /// An attribute in some other namespace is allowed anywhere and ignored, which is how a processor's
        /// own extensions are written. One in the XSLT namespace is not: the standard attributes take the
        /// prefix on a literal result element and never on an XSLT element, so <c>xsl:version</c> written on
        /// <c>xsl:template</c> is an error rather than a long-winded <c>version</c>.
        /// </remarks>
        private void ValidateXsltAttributes(int element, XsltElement shape)
        {
            int count = m_tree.AttributeCountOf(element);
            NameTable names = m_tree.NameTable;

            for (int i = 0; i < count; i++)
            {
                int attribute = m_tree.AttributeAt(element, i);
                int fingerprint = m_tree.FingerprintOf(attribute);
                string uri = names.GetNamespaceUri(fingerprint);

                if (uri.Length != 0 && uri != XsltNamespace)
                {
                    continue;
                }

                string name = names.GetLocalName(fingerprint);

                // A shadow attribute is the ordinary one with an underscore in front, so it is allowed
                // exactly where the ordinary one is — and only at 3.0, where the form exists at all.
                bool shadow = name.Length > 1 && name[0] == '_' && Implements30;
                string spelt = shadow ? name[1..] : name;

                // An attribute 3.0 added is refused below 3.0, the same rule the elements follow. A 2.0
                // stylesheet writing one is addressing a processor it does not have, and reading it anyway
                // would change what that stylesheet means.
                if (uri == XsltNamespace
                    || !XsltElements.Allows(shape, spelt)
                    || (XsltElements.AddedIn30(shape, spelt) && !Implements30))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0090,
                        $"'{QualifiedNameOf(element)}' has no '{name}' attribute in XSLT.");
                }

                // A shadow attribute's value is a template rather than the value, so what it may say is not
                // known here — the value it computes is checked wherever the attribute is read.
                if (shadow)
                {
                    continue;
                }

                string written = m_tree.StringValueOf(attribute);
                ValidateAttributeValue(element, name, written);

                if (name == "default-collation")
                {
                    RequireOneKnownCollation(written);
                }
            }

            foreach (string written in shape.Names)
            {
                if (GetAttribute(element, written) is not string value)
                {
                    continue;
                }

                if (!IsLexicalQName(value))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"'{value}' is not a name. The '{written}' of '{QualifiedNameOf(element)}' is a "
                        + "QName written out, not an expression and not an attribute value template.");
                }

                if (shape.Declares
                    && written == "name"
                    && value.IndexOf(':') >= 0
                    && m_tree.ResolvePrefix(element, value[..value.IndexOf(':')]) is string declaredIn
                    && Array.IndexOf(XsltElements.Reserved, declaredIn) >= 0
                    && !XsltElements.IsDefinedName(declaredIn, value[(value.IndexOf(':') + 1)..]))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0080,
                        $"'{value}' declares a name in '{declaredIn}', which the specifications reserve. A "
                        + "stylesheet's own names go in a namespace of its own.");
                }
            }
        }

        /// <summary>
        /// Refuses a <c>version</c> that is not the <c>xs:decimal</c> the attribute is declared to be.
        /// </summary>
        /// <remarks>
        /// The version decides which language the stylesheet is read in, so a value that is not a version at
        /// all cannot be shrugged off as naming a later one: <c>2.0e3</c> is not two thousand, it is a
        /// stylesheet whose author has not said what they wrote it against.
        /// </remarks>
        private static void RequireDecimal(string text)
        {
            ReadOnlySpan<char> digits = text.AsSpan().Trim();
            bool signed = digits.Length != 0 && digits[0] is '+' or '-';
            bool point = false;
            int count = 0;

            for (int i = signed ? 1 : 0; i < digits.Length; i++)
            {
                if (digits[i] == '.' && !point)
                {
                    point = true;
                }
                else if (char.IsAsciiDigit(digits[i]))
                {
                    count++;
                }
                else
                {
                    count = 0;
                    break;
                }
            }

            if (count == 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0110,
                    $"'{text}' is not a version. The attribute names one as a decimal number, so an "
                    + "exponent, a word or an empty value says nothing about which XSLT this is.");
            }
        }

        /// <summary>
        /// Refuses a <c>default-collation</c> that names nothing this engine has.
        /// </summary>
        /// <remarks>
        /// The value is a list of candidates, most preferred first, so a stylesheet may name a collation it
        /// would like and one it can settle for. It is an error only when none of them is recognised, which
        /// is the case where the stylesheet would silently be comparing strings some other way than it asked
        /// to.
        /// </remarks>
        private void RequireOneKnownCollation(string text)
        {
            foreach (Range candidate in text.AsSpan().Trim().Split(' '))
            {
                string uri = text.AsSpan().Trim()[candidate].ToString();

                if (uri.Length == 0)
                {
                    continue;
                }

                try
                {
                    Collation.Resolve(uri, m_options.CollationResolver);
                    return;
                }
                catch (XsltException)
                {
                    // Try the next candidate: the list is what a stylesheet will settle for, in order.
                }
            }

            throw XsltErrors.Error(
                XsltErrorCode.XTSE0125,
                $"'{text}' names no collation this engine has. A default-collation is what every comparison "
                + "in its scope will use, so one that cannot be resolved cannot be quietly ignored.");
        }

        private void ValidateAttributeValue(int element, string name, string value)
        {
            string[]? allowed = XsltElements.ValuesOf(name);

            // These attributes are token-typed, so surrounding whitespace is not part of the value.
            value = value.Trim();

            // XSLT 3.0 widened every yes/no attribute to the six spellings xs:boolean has, and the spellings
            // are vocabulary: a 3.0 processor reads disable-output-escaping="true" whatever version the
            // stylesheet claims. Reading it as a mistake would be answering in a language the processor is
            // not speaking — and ReadDeclarationFlag has always accepted all six, so refusing them here was
            // the table disagreeing with the code that reads them.
            if (allowed is not null && Implements30)
            {
                if (ReferenceEquals(allowed, XsltElements.YesNo))
                {
                    allowed = XsltElements.BooleanValues;
                }
                else if (ReferenceEquals(allowed, XsltElements.StandaloneValues))
                {
                    // standalone widened the same way, but it is not in the yes/no list: its third value,
                    // omit, is not a truth at all but the absence of one.
                    allowed = XsltElements.StandaloneValues30;
                }
            }

            // A value holding a curly brace is an attribute value template wherever the attribute is one, so
            // what it comes to is not known until the transformation runs.
            if (allowed is null || value.IndexOf('{') >= 0 || Array.IndexOf(allowed, value) >= 0)
            {
                return;
            }

            throw XsltErrors.Error(
                XsltErrorCode.XTSE0020,
                $"'{value}' is not one of the values '{name}' may take: {string.Join(", ", allowed)}.");
        }

        /// <summary>Whether the text is a QName as written — one name, or two joined by a colon.</summary>
        private static bool IsLexicalQName(string value)
        {
            value = value.Trim();

            // The XSLT 3.0 form, which carries its namespace rather than a prefix. Recognised here whatever
            // the version claims: the shape check answers whether this is a name at all, and telling a 2.0
            // stylesheet that Q{uri}local is not a name would be true but unhelpful — the version gate that
            // matters is in ResolveQualifiedName, where the name is actually read.
            if (value.StartsWith("Q{", StringComparison.Ordinal))
            {
                int close = value.IndexOf('}');
                return close > 0 && IsNCName(value[(close + 1)..]);
            }

            int colon = value.IndexOf(':');

            return colon < 0
                ? IsNCName(value)
                : IsNCName(value[..colon]) && IsNCName(value[(colon + 1)..]);
        }

        private static bool IsNCName(string value)
        {
            if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_'))
            {
                return false;
            }

            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) && character is not ('.' or '-' or '_' or '·'))
                {
                    return false;
                }
            }

            return true;
        }

        private void CompileXsltInstruction(int element, string localName, List<Instruction> output)
        {
            m_scopeElement = element;
            ValidateXsltElement(element, localName, XsltPlacement.Instruction);

            switch (localName)
            {
                case "value-of":
                {
                    // XSLT 2.0 lets the content stand in for the select, and requires one or the other:
                    // 1.0 has no content form at all, and only 3.0 allows an xsl:value-of that says nothing.
                    XsltVersion version = VersionOf(element);
                    Expr? select = CompileValueSelect(element);
                    Instruction[]? body = null;

                    if (select is null)
                    {
                        // What may stand in for the select is the processor's question rather than the
                        // stylesheet's claim. Content is a sequence constructor from 2.0 and 3.0 allows
                        // neither content nor select, whatever version the element writes: a version="1.0"
                        // on it asks for 1.0's behaviour, and does not narrow the content model back to the
                        // empty one XSLT 1.0 gave this instruction.
                        body = CompileSequence(element);

                        if (body.Length == 0 && !Implements30)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTSE0870,
                                "An xsl:value-of says what to write either with a select attribute or with "
                                + "its content, and this one does neither.");
                        }
                    }

                    // A separator is a 2.0 attribute, and writing one between items a 1.0 reading would have
                    // discarded makes no sense — so asking for one asks for the whole sequence, whatever
                    // version is in scope.
                    AttributeValueTemplate? separator =
                        OptionalAttributeValueTemplate(element, "separator");

                    output.Add(new ValueOfInstruction(
                        select,
                        body,
                        IsYes(GetAttribute(element, "disable-output-escaping")),
                        separator,
                        !version.IsBackwardsCompatible || separator is not null));

                    return;
                }

                case "text":
                {
                    // xsl:text is the one place whitespace survives verbatim. A text value template is still
                    // recognised in it — the element says how the text is treated, not whether it is text.
                    // An empty xsl:text is a zero-length text node — nothing once in a tree, but an item in
                    // a sequence, which is what count() of a variable declared as text()* sees, and what a
                    // function returning three of them returns three of.
                    string text = m_tree.StringValueOf(element);
                    {
                        int content = FirstIncludedChild(element);

                        output.Add(CompileText(
                            content >= 0 ? content : element,
                            text,
                            IsYes(GetAttribute(element, "disable-output-escaping"))));
                    }

                    return;
                }

                case "if":
                    output.Add(new IfInstruction(RequireExpression(element, "test"), CompileSequence(element)));
                    return;

                case "choose":
                    output.Add(CompileChoose(element));
                    return;

                case "for-each":
                {
                    Expr select = RequireExpression(element, "select");
                    SortKey[] sortKeys = CompileSortKeys(element);
                    output.Add(new ForEachInstruction(select, sortKeys, CompileSequenceSkippingSorts(element)));
                    return;
                }

                case "for-each-group":
                {
                    output.Add(CompileForEachGroup(element));
                    return;
                }

                case "analyze-string":
                {
                    output.Add(new AnalyzeStringInstruction(
                        RequireExpression(element, "select"),
                        RequireAttributeValueTemplate(element, "regex"),
                        OptionalAttributeValueTemplate(element, "flags"),
                        CompileBranch(element, "matching-substring"),
                        CompileBranch(element, "non-matching-substring"),
                        SyntaxVersion,
                        Version.IsBackwardsCompatible));

                    return;
                }

                case "sequence":
                {
                    Expr? select = OptionalExpression(element, "select");

                    // From 3.0 the value may be the content's rather than a select's — but not both, an
                    // xsl:fallback being the one child a select may stand beside.
                    if (select is null && Implements30)
                    {
                        output.Add(new SequenceInstruction(CompileSequence(element)));
                        return;
                    }

                    if (select is not null && Implements30 && HasContentBesidesFallback(element))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3185,
                            "An xsl:sequence has either a select attribute or content, and this one has both.");
                    }

                    // XSLT 2.0 gave the content beside a select no meaning at all — the value is the
                    // select's, and an xsl:fallback is the one thing that may stand inside — so it is
                    // content where the element allows none, which has a code of its own rather than
                    // 3.0's. Content with no select is read as 3.0 reads it whatever the version, since
                    // refusing it would refuse a stylesheet the suite expects to run and then fail on its
                    // type.
                    if (select is not null && HasContentBesidesFallback(element))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0010,
                            "An xsl:sequence takes its value from its select attribute, and in XSLT 2.0 it "
                            + "may hold nothing but an xsl:fallback.");
                    }

                    output.Add(new SequenceInstruction(select ?? RequireExpression(element, "select")));
                    return;
                }

                case "try" when Implements30:
                    output.Add(CompileTry(element));
                    return;

                case "where-populated" when Implements30:
                    output.Add(new WherePopulatedInstruction(CompileSequence(element)));
                    return;

                case "map" when Implements30:
                    output.Add(new MapInstruction(CompileSequence(element)));
                    return;

                case "merge" when Implements30:
                    output.Add(CompileMerge(element));
                    return;

                case "assert" when Implements30:
                    output.Add(new AssertInstruction(
                        RequireExpression(element, "test"),
                        GetAttribute(element, "select") is string said
                            ? ParseExpression(element, said)
                            : null,
                        CompileSequence(element)));
                    return;

                case "fork" when Implements30:
                    output.Add(new ForkInstruction(CompileSequence(element)));
                    return;

                case "source-document" when Implements30:
                    output.Add(new SourceDocumentInstruction(
                        RequireAttributeValueTemplate(element, "href"),
                        CompileSequence(element),
                        StaticBaseUri(element),
                        ReadUseAccumulators(element)));
                    return;

                case "map-entry" when Implements30:
                {
                    (Expr? value, Instruction[]? content) = CompileVariableValue(element);
                    output.Add(new MapEntryInstruction(
                        RequireExpression(element, "key"), value, content ?? Array.Empty<Instruction>()));

                    return;
                }

                // These two are recognised here only so that one standing outside any sequence constructor
                // that could answer them is not silently dropped. Where they belong, CompileSequence has
                // already taken them out and built a ConditionalSequenceInstruction around the lot.
                case "on-empty" or "on-non-empty" when Implements30:
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0010,
                        $"'{QualifiedNameOf(element)}' asks what the rest of the sequence constructor around "
                        + "it produced, and there is none here for it to ask about.");

                case "evaluate" when Implements30:
                    output.Add(CompileEvaluate(element));
                    return;

                case "iterate" when Implements30:
                    output.Add(CompileIterate(element));
                    return;

                case "next-iteration" when Implements30:
                    RequireTailPosition(element);
                    output.Add(CompileNextIteration(element));
                    return;

                case "break" when Implements30:
                    RequireTailPosition(element);

                    if (GetAttribute(element, "select") is not null && HasContent(element))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3125,
                            "xsl:break has both a select attribute and content. What it produces comes from "
                            + "one or the other.");
                    }

                    output.Add(new BreakInstruction(
                        GetAttribute(element, "select") is string broken
                            ? ParseExpression(element, broken)
                            : null,
                        CompileSequence(element)));
                    return;

                case "perform-sort":
                {
                    // Sorting without doing anything else: a for-each whose body passes on what it is given.
                    // What is sorted is the select, or from 2.0 the content — the xsl:sort children aside.
                    Expr select = OptionalExpression(element, "select")
                        ?? new SequenceConstructorExpr(CompileSequenceSkippingSorts(element));
                    output.Add(new ForEachInstruction(
                        select,
                        CompileSortKeys(element),
                        new Instruction[] { new SequenceInstruction(new ContextItemExpr(VersionOf(element))) }));

                    return;
                }

                case "next-match":
                    output.Add(new NextMatchInstruction(CompileWithParameters(element)));
                    return;

                case "result-document":
                {
                    RejectSchemaValidation(element);

                    // A result document is a document node, so validation validates it as one; strip and
                    // preserve validate nothing.
                    bool validateResult = false;
                    bool strictResult = false;
                    XdmSchemaType? resultType = null;

                    if (m_schemas is not null)
                    {
                        (string resultMode, XdmSchemaType? resultNamedType) = EffectiveValidation(element, literal: false);
                        resultType = resultNamedType;
                        strictResult = resultMode == "strict";
                        validateResult = resultNamedType is not null || resultMode is "strict" or "lax";
                    }

                    // Serialization attributes written here override what xsl:output settled for the
                    // principal result, so one transformation can write XML and HTML at once. A format names
                    // an xsl:output declaration to start from instead of the unnamed one.
                    // Every serialization attribute may be an attribute value template, and so may the format.
                    // A template is taken out of the static reading here and applied when the instruction
                    // runs, over the settings the rest produced — or, where the format itself is computed,
                    // over that format's, every written attribute being applied then too.
                    AttributeValueTemplate? format = OptionalAttributeValueTemplate(element, "format");
                    bool formatComputed = format is not null && format.ConstantValue is null;

                    OutputSettings settings = format?.ConstantValue is string formatName
                        ? NamedOutputSettings(element, formatName)
                        : OutputSettingsFor(CurrentPackage).With(m_options.OmitXmlDeclaration);

                    List<(string Name, AttributeValueTemplate Template)> templated = new();
                    m_templatedOutput.Clear();

                    foreach (string name in SerializationAttributes.Templated)
                    {
                        if (OptionalAttributeValueTemplate(element, name) is AttributeValueTemplate template
                            && (template.ConstantValue is null || formatComputed))
                        {
                            templated.Add((name, template));
                            m_templatedOutput.Add(name);
                        }
                    }

                    settings = ReadLocalOutputSettings(element, settings);
                    m_templatedOutput.Clear();

                    output.Add(new ResultDocumentInstruction(
                        OptionalAttributeValueTemplate(element, "href"),
                        settings,
                        CompileSequence(element),
                        Claims30(element),
                        templated.ToArray(),
                        formatComputed ? format : null,
                        formatComputed ? AllNamedOutputSettings() : null,
                        formatComputed || templated.Count != 0 ? PrefixesInScope() : null,
                        validateResult,
                        strictResult,
                        resultType));

                    return;
                }

                case "matching-substring":
                case "non-matching-substring":
                    // Handled by the xsl:analyze-string that owns them.
                    return;

                case "apply-templates":
                {
                    string? select = GetAttribute(element, "select");
                    Expr? selectExpression = select is null ? null : ParseExpression(element, select);
                    string? modeText = GetAttribute(element, "mode");
                    int mode = modeText switch
                    {
                        // Not a mode but an instruction to stay in the one already in force, which is only
                        // known while the transformation runs.
                        "#current" => CompiledStylesheet.CurrentMode,
                        _ => ResolveMode(element, modeText),
                    };

                    if (mode != CompiledStylesheet.CurrentMode)
                    {
                        RequireDeclaredMode(
                            mode, modeText is null ? "it does not name" : $"'{modeText}'", element);
                    }
                    output.Add(new ApplyTemplatesInstruction(
                        selectExpression,
                        mode,
                        CompileSortKeys(element),
                        CompileWithParameters(element),
                        Implements30));
                    return;
                }

                case "call-template":
                {
                    string name = GetAttribute(element, "name")
                        ?? throw new XsltException("An xsl:call-template must have a name.");

                    ExpandedName expanded = ResolveQualifiedName(element, name);
                    // xsl:original is not a name in the stylesheet's own space: it means the component this
                    // one replaced, so it is resolved against the template being compiled rather than
                    // looked up. Which is what keeps two overrides in one package apart — a global entry
                    // under that name would give both of them whichever was overridden last.
                    Template? target = expanded.Equals(XsltOriginal)
                        ? m_compilingTemplate?.Original
                        : m_namedTemplates.TryGetValue(expanded, out Template? named) ? named : null;

                    if (target is not null && !expanded.Equals(XsltOriginal))
                    {
                        CheckReference(
                            "template", expanded, -1, TemplatePackage(target), XsltErrorCode.XTSE0650, name);
                    }

                    if (target is null)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0650,
                            expanded.Equals(XsltOriginal)
                                ? "xsl:original names the component this one replaced, and this template "
                                    + "is not inside an xsl:override."
                                : $"No template is named '{name}'.");
                    }

                    WithParameter[] supplied = CompileWithParameters(element);

                    // Checked once every template body is compiled, since the one being called may not have
                    // been reached yet and its parameters are not known until it has.
                    m_pendingCalls.Add((target, supplied, name, VersionOf(element).IsBackwardsCompatible));

                    output.Add(new CallTemplateInstruction(target, supplied));
                    return;
                }

                case "variable":
                {
                    string name = GetAttribute(element, "name")
                        ?? throw new XsltException("An xsl:variable must have a name.");

                    ExpandedName expanded = ResolveQualifiedName(element, name);
                    XdmSequenceType? declared = ReadDeclaredType(element);
                    (Expr? select, Instruction[]? body) = CompileVariableValue(element);

                    int slot = m_frameSlotCount++;

                    // A variable is in scope for what follows it, so it is declared after its own value is
                    // compiled — a variable cannot refer to itself.
                    m_scope.Add(new VariableBinding(expanded, slot, false));
                    output.Add(new VariableInstruction(slot, select, body, declared, StaticBaseUri(element)));
                    return;
                }

                case "copy-of":
                {
                    bool preserveCopyOf = PreservesTypesOnCopy(element);
                    Instruction copyOf = new CopyOfInstruction(
                        RequireExpression(element, "select"),
                        ReadCopyNamespaces(element),
                        ReadDeclarationFlag(element, "copy-accumulators"),
                        preserveCopyOf);
                    output.Add(WithValidation(element, copyOf, ValidationShape.Copy));

                    return;
                }

                case "copy":
                {
                    ExpandedName[] sets = ReadAttributeSetNames(element);
                    Instruction copy = new CopyInstruction(
                        CompileSequence(element),
                        sets,
                        ReadCopyNamespaces(element),
                        OptionalExpression(element, "select"),
                        Claims30(element),
                        ReadDeclarationFlag(element, "copy-accumulators"),
                        ReadInheritNamespaces(element),
                        PreservesTypesOnCopy(element));
                    output.Add(WithValidation(element, copy, ValidationShape.Copy));

                    return;
                }

                case "element":
                {
                    ExpandedName[] sets = ReadAttributeSetNames(element);
                    Instruction elementInstruction = new ElementInstruction(
                        RequireAttributeValueTemplate(element, "name"),
                        OptionalAttributeValueTemplate(element, "namespace"),
                        CompileSequence(element),
                        NamespacesOn(element),
                        sets,
                        ReadInheritNamespaces(element));
                    output.Add(WithValidation(element, elementInstruction, ValidationShape.Element));
                    return;
                }

                case "attribute":
                {
                    Instruction attributeInstruction = new AttributeInstruction(
                        RequireAttributeValueTemplate(element, "name"),
                        OptionalAttributeValueTemplate(element, "namespace"),
                        CompileSequence(element),
                        NamespacesOn(element),
                        CompileValueSelect(element),
                        OptionalAttributeValueTemplate(element, "separator"));
                    output.Add(WithValidation(element, attributeInstruction, ValidationShape.Attribute));

                    return;
                }

                case "document":
                    output.Add(WithValidation(
                        element,
                        new DocumentInstruction(CompileSequence(element), StaticBaseUri(element)),
                        ValidationShape.Document));
                    return;

                case "namespace":
                {
                    string? select = GetAttribute(element, "select");
                    Instruction[] body = CompileSequence(element);

                    if (select is not null && body.Length != 0)
                    {
                        throw new XsltException(
                            "An xsl:namespace has both a select attribute and content. The URI comes from one "
                            + "or the other, so writing both leaves it ambiguous.");
                    }

                    output.Add(new NamespaceInstruction(
                        RequireAttributeValueTemplate(element, "name"),
                        select is null ? null : ParseExpression(element, select),
                        body));

                    return;
                }

                case "comment":
                    output.Add(new CommentInstruction(
                        CompileSequence(element), CompileValueSelect(element)));

                    return;

                case "processing-instruction":
                    output.Add(new ProcessingInstructionInstruction(
                        RequireAttributeValueTemplate(element, "name"),
                        CompileSequence(element),
                        CompileValueSelect(element)));

                    return;

                case "message":
                {
                    // terminate became an attribute value template in 2.0, so what it says may not be known
                    // until the message is reached. A value written out is settled here, which keeps the
                    // ordinary xsl:message free of an expression to evaluate.
                    string? terminate = GetAttribute(element, "terminate");
                    Instruction[] body = CompileSequence(element);
                    m_scopeElement = element;

                    // What to report may be given as an expression as well as as content, and the message
                    // is built from the two together — the expression first, then the constructor — so the
                    // select is simply the first thing the message constructs. Unlike xsl:value-of and
                    // xsl:comment, which take one or the other, this element takes both: the suite's
                    // version-017 writes <xsl:message select="'message 1: '">A message</xsl:message> and
                    // reads the message as "message 1: A message".
                    if (GetAttribute(element, "select") is string written)
                    {
                        Instruction[] both = new Instruction[body.Length + 1];

                        both[0] = new SequenceInstruction(ParseExpression(element, written));
                        Array.Copy(body, 0, both, 1, body.Length);
                        body = both;
                    }

                    // A terminating message may name the error it raises, which is the code a caller sees —
                    // computed or not, resolved with the prefixes in scope here.
                    AttributeValueTemplate? errorCode = Implements30
                        ? OptionalAttributeValueTemplate(element, "error-code")
                        : null;

                    MessageInstruction message = terminate is not null && terminate.IndexOf('{') >= 0
                        ? new MessageInstruction(
                            body, AttributeValueTemplate.Parse(terminate, this, m_backend), Implements30)
                        : new MessageInstruction(body, IsYes(terminate), Implements30);

                    if (errorCode is not null)
                    {
                        message.ErrorCode = errorCode;
                        message.Prefixes = PrefixesInScope();
                    }

                    output.Add(message);
                    return;
                }

                case "number":
                    output.Add(CompileNumber(element));
                    return;

                case "apply-imports":
                    // A rule declared inside an xsl:override has no imports to apply: what it stands in
                    // place of is reached by xsl:next-match, and the specification refuses the other.
                    if (IsInsideOverride(element))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE3460,
                            "xsl:apply-imports cannot be used in a template rule declared inside an "
                            + "xsl:override. The rule being overridden is reached by xsl:next-match.");
                    }

                    output.Add(new ApplyImportsInstruction(CompileWithParameters(element)));
                    return;

                case "sort":
                case "with-param":
                case "param":
                    // Handled by the instruction that owns them.
                    return;

                case "fallback":
                    // Reaching xsl:fallback here means its parent *was* implemented, so there is nothing to
                    // fall back from and it produces nothing. Only CompileFallback ever instantiates one.
                    return;

                default:
                    if (!IsForwardsCompatible(element))
                    {
                        throw new XsltException($"'xsl:{localName}' is not supported.");
                    }

                    // Under forwards-compatible processing the stylesheet is written for a later version of
                    // XSLT, so an instruction this engine has never heard of is expected rather than wrong.
                    output.Add(CompileFallback(
                        element,
                        $"'xsl:{localName}' is not an instruction this engine implements. Give it an "
                        + "xsl:fallback child to say what to do instead."));
                    return;
            }
        }

        /// <summary>
        /// Compiles the stand-in for an instruction this engine does not implement: its <c>xsl:fallback</c>
        /// children, and the error to report if it is reached without any.
        /// </summary>
        /// <remarks>
        /// Everything else inside the instruction is left uncompiled. It was written for a processor that
        /// understands the instruction, so what it means there is unknowable here — and it may well contain
        /// further unimplemented things, which would turn a stylesheet that runs perfectly well into one that
        /// refuses to compile.
        /// </remarks>
        private Instruction CompileFallback(int element, string message, XsltErrorCode? code = null)
        {
            List<Instruction> fallback = new List<Instruction>();
            bool given = false;

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string localName)
                    && localName == "fallback")
                {
                    // Recorded apart from what it compiled to. An empty xsl:fallback says to do nothing,
                    // which is a fallback and not the absence of one.
                    given = true;
                    fallback.AddRange(CompileSequence(child));
                }
            }

            m_scopeElement = element;
            return new FallbackInstruction(fallback.ToArray(), given, message, code);
        }

        /// <summary>Compiles <c>xsl:for-each-group</c>, whose four grouping attributes are mutually exclusive.</summary>
        private Instruction CompileForEachGroup(int element)
        {
            // The collation may be an attribute value template, and then what it names is not known until
            // the instruction runs. A written one is refused here as ever; a computed one travels with the
            // instruction and is refused there.
            AttributeValueTemplate? collation = OptionalAttributeValueTemplate(element, "collation");

            if (collation?.ConstantValue is string named)
            {
                ResolveKnownCollation(named.Trim(), XsltErrorCode.XTDE1110);
            }

            m_scopeElement = element;

            // No collation written, and the instruction groups under the default collation in scope
            // (§14.4): a default-collation on the template or the stylesheet reaches the grouping itself,
            // not only the comparisons written inside it.
            string? defaultCollation = collation is null && DefaultCollation != Collation.CodepointUri
                ? DefaultCollation
                : null;

            Expr select = RequireExpression(element, "select");

            (string Attribute, GroupingKind Kind)[] forms =
            {
                ("group-by", GroupingKind.ByKey),
                ("group-adjacent", GroupingKind.ByAdjacentKey),
                ("group-starting-with", GroupingKind.StartingWith),
                ("group-ending-with", GroupingKind.EndingWith),
            };

            string? written = null;
            GroupingKind kind = GroupingKind.ByKey;

            foreach ((string attribute, GroupingKind candidate) in forms)
            {
                if (GetAttribute(element, attribute) is null)
                {
                    continue;
                }

                if (written is not null)
                {
                    throw new XsltException(
                        $"An xsl:for-each-group has both '{written}' and '{attribute}', but a population can "
                        + "only be divided one way at a time.");
                }

                written = attribute;
                kind = candidate;
            }

            if (written is null)
            {
                throw new XsltException(
                    "An xsl:for-each-group must say how to group: group-by, group-adjacent, "
                    + "group-starting-with or group-ending-with.");
            }

            bool byKey = kind is GroupingKind.ByKey or GroupingKind.ByAdjacentKey;
            bool composite = ReadDeclarationFlag(element, "composite");

            // composite says how to read the key's values, so on a form that has no key it says nothing.
            if (composite && !byKey)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE1090,
                    $"An xsl:for-each-group has both 'composite' and '{written}'. Composite says that the "
                    + "values of a grouping key are one key together, and a pattern gives no values.");
            }

            m_scopeElement = element;
            Pattern[]? pattern = byKey ? null : Pattern.Parse(GetAttribute(element, written)!, this);

            return new ForEachGroupInstruction(
                select,
                byKey ? RequireExpression(element, written) : null,
                pattern,
                kind,
                composite,
                CompileSortKeys(element),
                CompileSequenceSkippingSorts(element),
                Implements30,
                collation,
                defaultCollation);
        }

        /// <summary>Compiles one branch of <c>xsl:analyze-string</c>, which may be absent.</summary>
        private Instruction[] CompileBranch(int element, string localName)
        {
            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Element
                    && IsXsltElement(child, out string name) && name == localName)
                {
                    return CompileSequence(child);
                }
            }

            return Array.Empty<Instruction>();
        }

        /// <summary>Whether text is nothing but XML whitespace.</summary>
        private static bool IsXmlWhitespace(string text)
        {
            foreach (char c in text)
            {
                if (c is not (' ' or '\t' or '\r' or '\n'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Refuses an <c>xsl:on-completion</c> that is not a direct child of its <c>xsl:iterate</c>.</summary>
        private void RequireOnCompletionAtTop(int iterate, int node)
        {
            for (int child = m_tree.FirstChildOf(node); child >= 0; child = m_tree.NextSiblingOf(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element)
                {
                    continue;
                }

                if (IsXsltElement(child, out string localName))
                {
                    if (localName == "on-completion" && node != iterate)
                    {
                        m_scopeElement = child;

                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0010,
                            "xsl:on-completion has to be a direct child of its xsl:iterate.");
                    }

                    // A nested xsl:iterate has completions of its own.
                    if (localName == "iterate")
                    {
                        continue;
                    }
                }

                RequireOnCompletionAtTop(iterate, child);
            }
        }

        /// <summary>Whether an element stands inside an <c>xsl:override</c>.</summary>
        private bool IsInsideOverride(int element)
        {
            for (int ancestor = m_tree.ParentOf(element); ancestor >= 0; ancestor = m_tree.ParentOf(ancestor))
            {
                if (m_tree.KindOf(ancestor) == NodeKind.Element
                    && IsXsltElement(ancestor, out string localName)
                    && localName == "override")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Refuses an <c>xsl:break</c> or <c>xsl:next-iteration</c> that is not in a tail position: last in
        /// its sequence constructor, and that constructor the body of the <c>xsl:iterate</c> or of an
        /// <c>xsl:if</c>, <c>xsl:when</c> or <c>xsl:otherwise</c> that is, and so on up.
        /// </summary>
        private void RequireTailPosition(int element)
        {
            bool enclosed = false;

            for (int ancestor = m_tree.ParentOf(element); ancestor >= 0; ancestor = m_tree.ParentOf(ancestor))
            {
                if (m_tree.KindOf(ancestor) == NodeKind.Element
                    && IsXsltElement(ancestor, out string enclosing)
                    && enclosing is "iterate" or "template" or "function")
                {
                    enclosed = enclosing == "iterate";
                    break;
                }
            }

            // One that no xsl:iterate encloses at all — reached through a called template, say — is not
            // misplaced within its iterate but outside any, which the specification counts as content the
            // element may not hold.
            if (!enclosed)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0010,
                    $"{QualifiedNameOf(element)} has to stand inside the xsl:iterate it ends, lexically: a "
                    + "template the iterate calls is not inside it.");
            }

            for (int node = element; ; )
            {
                bool branch = IsXsltElement(node, out string own) && own is "when" or "otherwise";

                if (HasContentAfter(
                    node, name => name is "fallback" or "catch" || (branch && name is "when" or "otherwise")))
                {
                    break;
                }

                int parent = m_tree.ParentOf(node);

                if (parent < 0 || m_tree.KindOf(parent) != NodeKind.Element
                    || !IsXsltElement(parent, out string localName))
                {
                    break;
                }

                if (localName == "iterate")
                {
                    return;
                }

                if (localName is not ("if" or "when" or "otherwise" or "choose" or "try" or "catch"))
                {
                    break;
                }

                node = parent;
            }

            throw XsltErrors.Error(
                XsltErrorCode.XTSE3120,
                $"{QualifiedNameOf(element)} is not in a tail position: it has to be the last thing its "
                + "xsl:iterate does in an iteration, reached through nothing but xsl:if, xsl:choose and "
                + "xsl:try.");
        }

        /// <summary>Whether an instruction has content other than an <c>xsl:fallback</c>.</summary>
        private bool HasContentBesidesFallback(int element)
        {
            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) == NodeKind.Text && IsXmlWhitespace(m_tree.StringValueOf(child)))
                {
                    continue;
                }

                if (m_tree.KindOf(child) != NodeKind.Element
                    || !IsXsltElement(child, out string localName)
                    || localName != "fallback")
                {
                    return true;
                }
            }

            return false;
        }

        private Expr? OptionalExpression(int element, string attributeName)
        {
            string? text = GetAttribute(element, attributeName);
            return text is null ? null : ParseExpression(element, text);
        }

        private Instruction CompileNumber(int element)
        {
            string? value = GetAttribute(element, "value");
            string? count = GetAttribute(element, "count");
            string? from = GetAttribute(element, "from");

            // A value says what number to write; count, level, select and from say how to work one out from
            // the source. Writing both asks two different questions and answers neither, so the
            // specification refuses it rather than picking one.
            if (value is not null)
            {
                foreach (string counting in new[] { "count", "level", "select", "from" })
                {
                    if (GetAttribute(element, counting) is not null)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0975,
                            $"This xsl:number has a value and also a '{counting}'. A value is the number to "
                            + "write; the others say how to count one out of the source.");
                    }
                }
            }

            NumberLevel level = GetAttribute(element, "level") switch
            {
                null or "single" => NumberLevel.Single,
                "multiple" => NumberLevel.Multiple,
                "any" => NumberLevel.Any,
                string other => throw new XsltException($"'{other}' is not a valid xsl:number level."),
            };

            // lang chooses the language a number is spelled in, which this engine has for English and German
            // and for nothing else; the specification's recovery for a language a processor does not have
            // is to use one it does. Not having a language is not the same as the value being misspelled,
            // though, so the value is still checked.
            bool alphabetic = GetAttribute(element, "letter-value") == "alphabetic";
            AttributeValueTemplate? language = OptionalAttributeValueTemplate(element, "lang");

            if (language?.ConstantValue is string written && !NumberInstruction.IsLanguage(written))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{written}' is not a language code, which the lang of xsl:number has to be.");
            }

            return new NumberInstruction(
                value is null ? null : ParseExpression(element, value),
                level,
                count is null ? null : Pattern.Parse(count, this),
                from is null ? null : Pattern.Parse(from, this),
                OptionalAttributeValueTemplate(element, "format"),
                OptionalAttributeValueTemplate(element, "grouping-separator"),
                OptionalAttributeValueTemplate(element, "grouping-size"),
                alphabetic,
                GetAttribute(element, "select") is string select ? ParseExpression(element, select) : null,
                OptionalAttributeValueTemplate(element, "ordinal"),
                language,
                VersionOf(element).IsBackwardsCompatible,
                CheckedStartAt(element));
        }

        /// <summary>The start-at of an xsl:number, a written one refused now where it is not an integer.</summary>
        private AttributeValueTemplate? CheckedStartAt(int element)
        {
            AttributeValueTemplate? startAt = OptionalAttributeValueTemplate(element, "start-at");

            // One integer per level, whitespace between them, and any that is not an integer is refused.
            if (startAt?.ConstantValue is string written
                && written.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(
                    level => !long.TryParse(level, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)))
            {
                m_scopeElement = element;

                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{written}' is not an integer, and the start-at of xsl:number is one.");
            }

            return startAt;
        }

        private Instruction CompileChoose(int element)
        {
            List<Expr> tests = new List<Expr>();
            List<Instruction[]> branches = new List<Instruction[]>();
            Instruction[]? otherwise = null;

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element || !IsXsltElement(child, out string localName))
                {
                    continue;
                }

                if (localName == "when")
                {
                    m_scopeElement = child;
                    tests.Add(RequireExpression(child, "test"));
                    branches.Add(CompileSequence(child));
                }
                else if (localName == "otherwise")
                {
                    otherwise = CompileSequence(child);
                }
            }

            if (tests.Count == 0)
            {
                throw new XsltException("An xsl:choose must contain at least one xsl:when.");
            }

            return new ChooseInstruction(tests.ToArray(), branches.ToArray(), otherwise);
        }

        /// <summary>
        /// Compiles the sort keys of an instruction.
        /// </summary>
        /// <param name="element">The instruction the keys belong to.</param>
        /// <param name="wanted">
        /// Which child names the keys: <c>sort</c> everywhere but an <c>xsl:merge-source</c>, whose
        /// <c>xsl:merge-key</c> carries the same attributes under a name that says what it decides.
        /// </param>
        /// <summary>A written ordering attribute of <c>xsl:sort</c> with a value it may not take.</summary>
        private XsltException SortAttribute(int element, string attribute, string value)
        {
            m_scopeElement = element;

            return XsltErrors.Error(
                XsltErrorCode.XTSE0020,
                $"'{value}' is not a value the {attribute} attribute of xsl:sort may take.");
        }

        private SortKey[] CompileSortKeys(int element, string wanted = "sort")
        {
            List<SortKey> keys = new List<SortKey>();

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element
                    || !IsXsltElement(child, out string localName)
                    || localName != wanted)
                {
                    continue;
                }

                m_scopeElement = child;

                if (wanted == "merge-key")
                {
                    RequireKnownCollation(child, XsltErrorCode.XTDE1035);
                }

                // The same fault xsl:sort has its own code for, one element along. A merge key comes from
                // the attribute or from the content, and a declaration writing both says two things.
                if (wanted == "merge-key"
                    && GetAttribute(child, "select") is not null
                    && HasContent(child))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3200,
                        "xsl:merge-key has both a select attribute and content. The key comes from one or "
                        + "the other.");
                }

                // The key is the select, or from 2.0 the content — one or the other, XTSE1015 being both.
                Expr select;
                if (GetAttribute(child, "select") is string written)
                {
                    if (wanted == "sort" && HasContent(child))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE1015,
                            "xsl:sort has both a select attribute and content. The key comes from one or the "
                            + "other.");
                    }

                    select = ParseExpression(child, written);
                }
                else if (HasContent(child))
                {
                    // xsl:merge-key takes content as xsl:sort does, and evaluates it in temporary output state.
                    select = new SequenceConstructorExpr(CompileSequence(child));
                    m_scopeElement = child;
                }
                else
                {
                    select = ParseExpression(child, ".");
                }

                bool compatible = VersionOf(child).IsBackwardsCompatible;

                // Each ordering attribute is a value, settled now, or an attribute value template, settled
                // once per sort with the focus of the instruction that sorts. A written value that is not one
                // the attribute may take is refused here; a computed one is XTDE0030 when it is computed.
                AttributeValueTemplate? order = OptionalAttributeValueTemplate(child, "order");
                AttributeValueTemplate? dataType = OptionalAttributeValueTemplate(child, "data-type");
                AttributeValueTemplate? lang = OptionalAttributeValueTemplate(child, "lang");
                AttributeValueTemplate? caseOrderTemplate = OptionalAttributeValueTemplate(child, "case-order");
                AttributeValueTemplate? collationTemplate = OptionalAttributeValueTemplate(child, "collation");

                bool descending = order?.ConstantValue?.Trim() switch
                {
                    null or "ascending" => false,
                    "descending" => true,
                    string other => throw SortAttribute(child, "order", other),
                };

                string? dataTypeWritten = dataType?.ConstantValue?.Trim();
                bool numeric = dataTypeWritten == "number";
                // A backwards-compatible stylesheet sorts by text whatever the values are, XSLT 1.0's
                // data-type defaulting to text. The suite's backwards-012 reads XSLT 2.0 §3.9 as changing
                // only what the key is and not how it is compared, and wants a numeric key sorted
                // numerically; a 1.0 processor does not, and neither does the oracle this engine's own
                // sorting is held against. The stylesheet said 1.0 and gets 1.0.
                bool asText = compatible || dataTypeWritten == "text";

                if (dataTypeWritten is not (null or "number" or "text") && !dataTypeWritten.Contains(':'))
                {
                    throw SortAttribute(child, "data-type", dataTypeWritten);
                }

                string? langWritten = lang?.ConstantValue?.Trim();
                if (langWritten is not null && !SortKey.IsLanguage(langWritten))
                {
                    throw SortAttribute(child, "lang", langWritten);
                }

                SortCaseOrder caseOrder = caseOrderTemplate?.ConstantValue?.Trim() switch
                {
                    "upper-first" => SortCaseOrder.UpperFirst,
                    "lower-first" => SortCaseOrder.LowerFirst,
                    null => SortCaseOrder.Unspecified,
                    string other => throw SortAttribute(child, "case-order", other),
                };

                Collation? collation = collationTemplate?.ConstantValue is string uri
                    ? SortKey.ResolveCollation(uri.Trim(), m_options.CollationResolver)
                    : null;

                // The attribute names a collation, a lang names one, and where neither does the default
                // collation in scope decides (§13.1.3). Only where it is not the code point collation: that
                // is what the ordinary comparison does anyway, and setting it would take an xsl:sort with a
                // case-order but no lang off the path that reads one. A merge key is ordered the same way.
                if (collation is null
                    && langWritten is null
                    && DefaultCollation != Collation.CodepointUri)
                {
                    collation = SortKey.ResolveCollation(DefaultCollation, m_options.CollationResolver);
                }

                bool computed = wanted == "sort"
                    && (order?.ConstantValue is null && order is not null
                        || dataType?.ConstantValue is null && dataType is not null
                        || lang?.ConstantValue is null && lang is not null
                        || caseOrderTemplate?.ConstantValue is null && caseOrderTemplate is not null
                        || collationTemplate?.ConstantValue is null && collationTemplate is not null);

                keys.Add(new SortKey(
                    select,
                    numeric,
                    descending,
                    SortKey.CultureFor(langWritten),
                    caseOrder,
                    compatible,
                    collation,
                    computed
                        ? new SortOrdering(
                            order?.ConstantValue is null ? order : null,
                            dataType?.ConstantValue is null ? dataType : null,
                            lang?.ConstantValue is null ? lang : null,
                            caseOrderTemplate?.ConstantValue is null ? caseOrderTemplate : null,
                            collationTemplate?.ConstantValue is null ? collationTemplate : null)
                        : null,
                    asText));
            }

            m_scopeElement = element;
            return keys.ToArray();
        }

        /// <summary>
        /// Compiles a body, leaving out the <c>xsl:sort</c> children, which belong to the enclosing
        /// instruction rather than to its content.
        /// </summary>
        private Instruction[] CompileSequenceSkippingSorts(int element)
        {
            return CompileSequence(element, name => name == "sort");
        }

        private WithParameter[] CompileWithParameters(int element)
        {
            List<WithParameter> parameters = new List<WithParameter>();

            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                if (m_tree.KindOf(child) != NodeKind.Element
                    || !IsXsltElement(child, out string localName)
                    || localName != "with-param")
                {
                    continue;
                }

                m_scopeElement = child;
                string name = GetAttribute(child, "name")
                    ?? throw new XsltException("An xsl:with-param must have a name.");

                ExpandedName expanded = ResolveQualifiedName(child, name);
                bool tunnel = ReadDeclarationFlag(child, "tunnel");

                // Two parameters of one name on one call: the second would silently win, and which of two
                // values the callee was meant to see is not something a processor gets to decide. Tunnel and
                // ordinary parameters are separate, so one name may appear once in each.
                foreach (WithParameter earlier in parameters)
                {
                    if (earlier.Name == expanded && earlier.Tunnel == tunnel)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0670,
                            $"'{name}' is supplied twice on one call. A parameter can be given one value.");
                    }
                }

                (Expr? select, Instruction[]? body) = CompileVariableValue(child);
                parameters.Add(
                    new WithParameter(expanded, select, body, tunnel, ReadDeclaredType(child))
                    {
                        BaseUri = StaticBaseUri(child),
                    });
            }

            m_scopeElement = element;
            return parameters.ToArray();
        }

        /// <summary>
        /// Reads the <c>mode</c> attribute of an <c>xsl:template</c>, which XSLT 2.0 lets name several.
        /// </summary>
        /// <remarks>
        /// <c>#default</c> stands for the mode a template with no <c>mode</c> attribute belongs to, and
        /// <c>#all</c> for every mode there is — which is why it cannot simply be turned into a list here.
        /// </remarks>
        /// <param name="element">The template.</param>
        /// <param name="mode">The attribute as written.</param>
        private (int[] Modes, bool EveryMode) ResolveModeList(int element, string? mode)
        {
            if (mode is null)
            {
                return (new[] { DefaultModeIn(element) }, false);
            }

            string[] tokens = mode.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0550, "The mode attribute of an xsl:template names no mode.");
            }

            if (Array.IndexOf(tokens, "#all") >= 0)
            {
                if (tokens.Length != 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0550,
                        "A template declared mode=\"#all\" already applies in every mode, so naming others "
                        + "alongside it says nothing.");
                }

                return (Array.Empty<int>(), true);
            }

            int[] modes = new int[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                // Each token is a mode name, and naming one twice is a stylesheet saying the same thing
                // twice — which the specification refuses rather than reads as once.
                bool token = tokens[i] == "#default"
                    || (tokens[i] == "#unnamed" && Implements30);

                if (!token && !IsLexicalQName(tokens[i]))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0550,
                        $"'{tokens[i]}' is not a mode name. A mode is a QName, or '#default'"
                        + (Implements30 ? ", or '#unnamed'" : string.Empty)
                        + ", or the whole attribute is '#all'.");
                }

                modes[i] = ResolveMode(element, tokens[i]);

                // A mode a template rule of the top-level package names is one the transformation may start
                // in where the package declares no modes (§2.3.4), which is what this records.
                if (PackageOf(m_tree) == 0)
                {
                    m_ruleModes.Add(modes[i]);
                }

                if (Array.IndexOf(modes, modes[i], 0, i) >= 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0550,
                        $"The mode attribute names '{tokens[i]}' twice.");
                }
            }

            return (modes, false);
        }

        /// <summary>
        /// Reads an <c>xsl:mode</c> declaration.
        /// </summary>
        /// <remarks>
        /// A mode exists whether or not it is declared, so this does not create one — it records what the
        /// stylesheet says about one, and creates the mode only as a side effect of naming it. Import
        /// precedence settles a disagreement the ordinary way, the later and higher-precedence declaration
        /// winning; two at one precedence are a stylesheet saying two things about one mode, which is
        /// <c>XTSE0035</c>.
        /// </remarks>
        private void ReadMode(int element, int precedence)
        {
            // A QName written as an attribute value carries whatever whitespace the stylesheet's own layout
            // left around it, and the XML parser does not take it off for us.
            string? name = GetAttribute(element, "name")?.Trim();

            if (name is not null && name != "#default" && !IsLexicalQName(name))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{name}' is not a mode name. A mode is a QName, or '#default' for the unnamed one.");
            }

            // Deliberately not through the default mode. An xsl:mode with no name declares the unnamed mode,
            // and says so in as many words; letting a default-mode in scope redirect it would mean a package
            // that set one could not declare the unnamed mode at all.
            int mode = name is null or "#default"
                ? CompiledStylesheet.DefaultMode
                : ResolveMode(element, name);

            // Checked here rather than left to the element table, which is switched off entirely while this
            // engine claims 2.0 and a stylesheet saying 3.0 is read forwards-compatibly. Every 3.0 feature
            // implemented before the version claim moves has to validate what it reads, or an unrecognised
            // value falls into whichever branch happens to be last.
            string? said = GetAttribute(element, "on-no-match")?.Trim();

            OnNoMatch? onNoMatch = said switch
            {
                null => null,
                "text-only-copy" => OnNoMatch.TextOnlyCopy,
                "shallow-copy" => OnNoMatch.ShallowCopy,
                "deep-copy" => OnNoMatch.DeepCopy,
                "shallow-skip" => OnNoMatch.ShallowSkip,
                "deep-skip" => OnNoMatch.DeepSkip,
                "fail" => OnNoMatch.Fail,
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{said}' is not one of the values 'on-no-match' may take: text-only-copy, "
                    + "shallow-copy, deep-copy, shallow-skip, deep-skip, fail."),
            };

            bool? warnOnNoMatch = GetAttribute(element, "warning-on-no-match") is null
                ? null
                : ReadDeclarationFlag(element, "warning-on-no-match");

            string? onMultipleMatch = GetAttribute(element, "on-multiple-match")?.Trim();

            if (onMultipleMatch is not (null or "use-last" or "fail"))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{onMultipleMatch}' is not one of the values on-multiple-match may take: use-last, fail.");
            }

            // A mode is public, private or final — never abstract, there being nothing in a mode to leave
            // undefined — and the unnamed mode is private to its package by definition (§3.5.3.1), so a
            // declaration saying otherwise of it is an error rather than a request.
            string? visibility = GetAttribute(element, "visibility")?.Trim();

            if (visibility is not (null or "public" or "private" or "final"))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{visibility}' is not a visibility a mode may have: public, private or final.");
            }

            if (visibility is "public" or "final" && mode == CompiledStylesheet.DefaultMode)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    "The unnamed mode is private to its package, and cannot be declared "
                    + $"visibility=\"{visibility}\".");
            }

            string? typedSaid = GetAttribute(element, "typed")?.Trim();

            // What the mode admits (§6.6.2): yes, strict and lax take typed nodes alone, no takes untyped
            // nodes alone, and unspecified takes either, which is also what saying nothing means.
            bool? typed = typedSaid switch
            {
                null or "unspecified" => null,
                "yes" or "true" or "1" or "strict" or "lax" => true,
                "no" or "false" or "0" => false,
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{typedSaid}' is not one of the values 'typed' may take: yes, no, strict, lax, "
                    + "unspecified."),
            };

            // Only a named mode is a component. The unnamed one is private to its package by definition,
            // which is why xsl:expose refuses to name it rather than failing to find it.
            if (mode != CompiledStylesheet.DefaultMode)
            {
                m_components.Add(new PackageComponent(
                    "mode",
                    ResolveQualifiedName(element, name!),
                    -1,
                    DeclaredVisibility(element),
                    null,
                    m_package));
            }

            // Kept at every precedence, because the declarations of one mode are merged attribute by
            // attribute: each attribute is settled by the highest precedence that writes it, and two
            // disagreeing about it is an error only when they are both at that precedence. BuildModes
            // decides, once every module is read — a module read later may still be about to override the
            // lot, which is the ordinary way of overriding a mode rather than a conflict.
            if (!m_modeDeclarations.TryGetValue(mode, out ModeStatement? stated))
            {
                m_modeDeclarations[mode] = stated = new ModeStatement(name ?? "#default");
            }

            stated.Declarations.Add(
                new ModeAttributes(
                    precedence,
                    onNoMatch,
                    warnOnNoMatch,
                    new ModuleElement(m_tree, element),
                    onMultipleMatch,
                    visibility,
                    typed,
                    typedSaid == "strict"));
        }

        /// <summary>
        /// Reads a <c>use-accumulators</c> attribute: which accumulators apply to a document this element
        /// makes available.
        /// </summary>
        /// <remarks>
        /// Saying nothing means <em>none</em>. Every accumulator a document is to carry has to be asked for
        /// by name, or all of them by <c>#all</c> — which is the whole point of the attribute, a streaming
        /// processor having to know before it reads a document what it will be asked about it. The documents
        /// a stylesheet never has to ask for are the ones nothing streams: what <c>doc()</c> reads and what
        /// the stylesheet builds itself carry every accumulator without being told.
        /// </remarks>
        /// <param name="element">The element carrying the attribute.</param>
        /// <param name="written">
        /// The attribute value, where it has already been read; otherwise it is read from the element.
        /// </param>
        private AccumulatorSet ReadUseAccumulators(int element, string? written = null)
        {
            written ??= GetAttribute(element, "use-accumulators");

            if (written is null)
            {
                return AccumulatorSet.None;
            }

            List<int> applicable = new List<int>();

            foreach (string token in written.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "#all")
                {
                    return AccumulatorSet.All;
                }

                ExpandedName named = ResolveQualifiedName(element, token);

                // No code: the specification makes this a static error but this engine has not been able to
                // find which one, and a wrong code is worse than none. The complaint is still made.
                if (!m_accumulatorsByName.TryGetValue(Scoped(named), out AccumulatorDefinition? declared))
                {
                    throw new XsltException(
                        $"use-accumulators names '{token}', and the stylesheet declares no xsl:accumulator "
                        + "of that name.");
                }

                applicable.Add(declared.Index);
            }

            return AccumulatorSet.Of(applicable);
        }

        /// <summary>
        /// Raises the disagreements between <c>xsl:mode</c> declarations that nothing settled.
        /// </summary>
        /// <remarks>
        /// Run once every module has been read, which is the earliest anything can be said: a stylesheet may
        /// import two modules that declare one mode differently and then declare it itself, and that is not
        /// a conflict but the ordinary way of overriding one.
        /// </remarks>
        private void BuildModes()
        {
            XdmTree reading = m_tree;

            foreach ((int mode, ModeStatement stated) in m_modeDeclarations)
            {
                // Highest precedence first, so the first declaration writing an attribute is the one that
                // settles it. A stable sort keeps the order they were read in among equals.
                List<ModeAttributes> declarations = stated.Declarations
                    .OrderByDescending(declaration => declaration.Precedence)
                    .ToList();

                int by = Settled(
                    stated,
                    declarations,
                    "on-no-match",
                    declaration => declaration.OnNoMatch is not null,
                    (one, other) => one.OnNoMatch == other.OnNoMatch);

                int warned = Settled(
                    stated,
                    declarations,
                    "warning-on-no-match",
                    declaration => declaration.WarnOnNoMatch is not null,
                    (one, other) => one.WarnOnNoMatch == other.WarnOnNoMatch);

                int multiple = Settled(
                    stated,
                    declarations,
                    "on-multiple-match",
                    declaration => declaration.OnMultipleMatch is not null,
                    (one, other) => one.OnMultipleMatch == other.OnMultipleMatch);

                // Settled like the rest, so that two declarations at one precedence disagreeing about a
                // mode's visibility are the error they are (XTSE0545) rather than two components.
                _ = Settled(
                    stated,
                    declarations,
                    "visibility",
                    declaration => declaration.Visibility is not null,
                    (one, other) => one.Visibility == other.Visibility);

                int typed = Settled(
                    stated,
                    declarations,
                    "typed",
                    declaration => declaration.Typed is not null,
                    (one, other) => one.Typed == other.Typed);

                // Resolved here rather than where the declaration was read, because an xsl:mode may name an
                // accumulator that a later module declares — or a later line of the same one. And compared
                // after the names have been resolved, because what two declarations have to agree about is
                // which accumulators they name — not the order they wrote them in, nor which prefix each
                // was written with.
                int used = Settled(
                    stated,
                    declarations,
                    "use-accumulators",
                    declaration => Writes(declaration.Written, "use-accumulators"),
                    (one, other) => AccumulatorsOf(one.Written).Equals(AccumulatorsOf(other.Written)));

                m_modeRules[mode] = new ModeDeclaration(
                    by < 0 ? OnNoMatch.TextOnlyCopy : declarations[by].OnNoMatch!.Value,
                    warned >= 0 && declarations[warned].WarnOnNoMatch!.Value,
                    multiple >= 0 && declarations[multiple].OnMultipleMatch == "fail",
                    typed >= 0 ? declarations[typed].Typed : null,
                    typed >= 0 && declarations[typed].StrictlyTyped);
                m_modeAccumulators[mode] = used < 0
                    ? AccumulatorSet.None
                    : AccumulatorsOf(declarations[used].Written);
            }

            m_tree = reading;
        }

        /// <summary>Reads one declaration's <c>use-accumulators</c>, in the module it was written in.</summary>
        /// <param name="written">Where the declaration is.</param>
        /// <summary>
        /// Finds which declaration of a mode settles one attribute: the first, in precedence order, to
        /// write it.
        /// </summary>
        /// <param name="stated">The mode, for the message.</param>
        /// <param name="declarations">Its declarations, highest precedence first.</param>
        /// <param name="attribute">The attribute's name, for the message.</param>
        /// <param name="writes">Whether a declaration writes the attribute at all.</param>
        /// <param name="agree">Whether two declarations that write it say the same thing.</param>
        /// <returns>The index of the settling declaration, or -1 where none writes the attribute.</returns>
        /// <exception cref="XsltException">
        /// <c>XTSE0545</c> where two declarations at the settling precedence disagree.
        /// </exception>
        private static int Settled(
            ModeStatement stated,
            List<ModeAttributes> declarations,
            string attribute,
            Func<ModeAttributes, bool> writes,
            Func<ModeAttributes, ModeAttributes, bool> agree)
        {
            for (int i = 0; i < declarations.Count; i++)
            {
                if (!writes(declarations[i]))
                {
                    continue;
                }

                for (int j = i + 1;
                    j < declarations.Count && declarations[j].Precedence == declarations[i].Precedence;
                    j++)
                {
                    if (writes(declarations[j]) && !agree(declarations[i], declarations[j]))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0545,
                            $"Two xsl:mode declarations of the same import precedence disagree about the "
                            + $"{attribute} of mode '{stated.Name}', and nothing of higher precedence "
                            + "settles it.");
                    }
                }

                return i;
            }

            return -1;
        }

        /// <summary>Whether a declaration in some module writes an attribute, in either form.</summary>
        private bool Writes(ModuleElement written, string attribute)
        {
            m_tree = written.Tree;

            return GetAttribute(written.Element, attribute) is not null;
        }

        private AccumulatorSet AccumulatorsOf(ModuleElement written)
        {
            m_tree = written.Tree;
            m_scopeElement = written.Element;

            return ReadUseAccumulators(written.Element);
        }

        /// <summary>What each mode's <c>xsl:mode</c> declarations settled on.</summary>
        private readonly Dictionary<int, ModeDeclaration> m_modeRules = new();

        /// <summary>Which accumulators apply to a document the transformation begins on, by mode.</summary>
        /// <remarks>
        /// Only the modes that narrowed it. A mode saying nothing is every accumulator, which is what a
        /// missing entry means and what all but a handful of stylesheets want.
        /// </remarks>
        private readonly Dictionary<int, AccumulatorSet> m_modeAccumulators = new();

        /// <summary>Every mode a package required to declare its modes has used but not declared.</summary>
        /// <remarks>
        /// Collected as the modes are resolved and checked once at the end, because a mode may be declared
        /// anywhere in the package — after the template that uses it, or in another module of it.
        /// </remarks>
        private readonly List<(int Mode, string Written, int Element)> m_modesToDeclare = new();

        /// <summary>
        /// Records a mode that <c>declared-modes</c> requires an <c>xsl:mode</c> for.
        /// </summary>
        /// <remarks>
        /// A package declares its modes by default, and the point of it is that a misspelt mode name is
        /// otherwise a new mode nothing reaches rather than a mistake. So the unnamed mode counts too: a
        /// package whose rules are all in named modes and which then writes one rule without a mode has
        /// almost certainly forgotten one.
        /// </remarks>
        /// <param name="mode">The mode's index.</param>
        /// <param name="written">The name as written, for the message.</param>
        /// <param name="element">The element that used it.</param>
        private void RequireDeclaredMode(int mode, string written, int element)
        {
            if (DeclaresItsModes())
            {
                m_modesToDeclare.Add((mode, written, element));
            }
        }

        /// <summary>
        /// Notes a mode a template rule put itself in without an <c>xsl:mode</c> for it, so that the mode
        /// counts as a component this package declared.
        /// </summary>
        /// <remarks>
        /// A package that does not declare its modes declares one by writing a rule in it, and that is a
        /// component like any other: written outside an xsl:override, alongside a public mode of the same
        /// name from a package this one uses, it is a second component of one name in view
        /// (<c>XTSE3050</c>). Which is why rules are added to a used package's mode inside xsl:override.
        /// </remarks>
        private void NoteImplicitMode(int mode, int element)
        {
            if (!Implements30 || mode == CompiledStylesheet.DefaultMode || DeclaresItsModes() || IsOverriding(element))
            {
                return;
            }

            if (!m_implicitModes.Exists(noted => noted.Package == m_package && noted.Mode == mode))
            {
                m_implicitModes.Add((m_package, mode, new ModuleElement(m_tree, element)));
            }
        }

        /// <summary>Turns the modes rules declared by being in them into components, where no xsl:mode did.</summary>
        private void DeclareImplicitModes()
        {
            foreach ((int package, int mode, ModuleElement source) in m_implicitModes)
            {
                ExpandedName name = ModeName(mode);

                if (FindComponent("mode", name, -1, package) is null)
                {
                    m_components.Add(new PackageComponent("mode", name, -1, null, null, package) { Element = source });
                }
            }
        }

        /// <summary>Whether the module being read is a package that has to declare every mode it uses.</summary>
        private bool DeclaresItsModes()
        {
            if (!Implements30 || !m_declaringModes.TryGetValue(m_tree, out bool declares))
            {
                return false;
            }

            return declares;
        }

        /// <summary>Which module trees belong to a package requiring its modes to be declared.</summary>
        private readonly Dictionary<XdmTree, bool> m_declaringModes = new();

        /// <summary>The modes template rules of the top-level package name.</summary>
        private readonly HashSet<int> m_ruleModes = new();

        /// <summary>
        /// The named modes a transformation may start in (§2.3.4): declared in the top-level package with
        /// public or final visibility, accepted into it with one, or — where the package declares no modes —
        /// named by one of its template rules and declared nothing else. The unnamed mode and the package's
        /// default mode are always eligible, and are not listed here.
        /// </summary>
        private HashSet<int> EligibleInitialModes(XdmTree principal)
        {
            HashSet<int> eligible = new HashSet<int>();

            // The modes an xsl:mode in the top-level package declares — as against the component a template
            // rule's mode gets implicitly, which says nothing about how the mode was meant to be reached.
            HashSet<int> declared = new HashSet<int>();

            foreach ((int mode, ModeStatement stated) in m_modeDeclarations)
            {
                foreach (ModeAttributes declaration in stated.Declarations)
                {
                    if (PackageOf(declaration.Written.Tree) == 0)
                    {
                        declared.Add(mode);
                    }
                }
            }

            foreach (PackageComponent component in m_components)
            {
                if (component.Kind != "mode")
                {
                    continue;
                }

                // A plain xsl:stylesheet is an implicit package whose declarations are public unless they say
                // otherwise, as ReadVisibility has it; a package's are private unless they say or an
                // xsl:expose says, which is what Effective holds. A used package's are seen through what
                // this one accepted.
                Visibility? seen;

                if (component.Package == 0)
                {
                    seen = component.Declared ?? (m_isPackage ? component.Effective : Visibility.Public);

                    // An xsl:expose making a mode private has said how it is to be reached, as an xsl:mode
                    // would have: not from outside, whatever the package's template rules name.
                    if (component.Exposed && seen == Visibility.Private && m_modes.TryGetValue(component.Name, out int hidden))
                    {
                        declared.Add(hidden);
                    }
                }
                else
                {
                    seen = VisibleAs(component, 0);
                }

                if (seen is Visibility.Public or Visibility.Final
                    && m_modes.TryGetValue(component.Name, out int mode))
                {
                    eligible.Add(mode);
                }
            }

            if (m_declaringModes.TryGetValue(principal, out bool declaring) && !declaring)
            {
                foreach (int mode in m_ruleModes)
                {
                    if (!declared.Contains(mode))
                    {
                        eligible.Add(mode);
                    }
                }
            }

            return eligible;
        }

        /// <summary>Checks every mode a package used against the ones it declared.</summary>
        /// <summary>
        /// Holds every template rule in a mode declared <c>typed="strict"</c> to a pattern whose first step
        /// names an element the schemas in scope declare (§6.6.2, <c>XTSE3105</c>).
        /// </summary>
        /// <remarks>
        /// A mode that takes only strictly validated nodes can only ever see elements the schema declares
        /// at the top level, so a rule matching any other name could never fire; saying so when the
        /// stylesheet is compiled is more use than never matching at run time.
        /// </remarks>
        private void CheckStrictlyTypedModePatterns()
        {
            if (m_schemas is null)
            {
                return;
            }

            foreach (TemplateRule rule in m_rules)
            {
                if (!m_modeRules.TryGetValue(rule.Mode, out ModeDeclaration mode) || !mode.StrictlyTyped)
                {
                    continue;
                }

                int slot = rule.Pattern.OutermostElementNameSlot;

                if (slot < 0)
                {
                    continue;
                }

                ExpandedName name = m_names.GetName(slot);

                if (m_schemas.FindElement(name.NamespaceUri, name.LocalName) is null)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE3105,
                        $"A template rule in a mode declared typed=\"strict\" matches '{name.LocalName}', "
                        + "and the schemas in scope declare no top-level element of that name. Only a node "
                        + "the schema declares can be validated strictly, so the rule could never match.");
                }
            }
        }

        private void CheckDeclaredModes()
        {
            foreach ((int mode, string written, int element) in m_modesToDeclare)
            {
                if (m_modeDeclarations.ContainsKey(mode))
                {
                    continue;
                }

                m_scopeElement = element;

                throw XsltErrors.Error(
                    XsltErrorCode.XTSE3085,
                    $"This package uses the mode {written}, and declares its modes, but no xsl:mode "
                    + "declares that one. Add the declaration, or say declared-modes=\"no\" on the "
                    + "xsl:package — the point of declaring them is that a misspelt mode name is otherwise "
                    + "a new mode nothing reaches rather than a mistake.");
            }
        }

        /// <summary>
        /// Every <c>xsl:mode</c> declaration of one mode written at the highest precedence any of them was.
        /// </summary>
        /// <remarks>
        /// All of them rather than the last, because whether two of them disagree is a question that cannot
        /// be answered while they are being read: what one says about its accumulators is a list of names,
        /// and those names cannot be resolved until every module has declared what it declares.
        /// </remarks>
        private sealed class ModeStatement
        {
            /// <summary>Initializes the declarations of one mode.</summary>
            /// <param name="name">The mode's name, for the message where two of them disagree.</param>
            public ModeStatement(string name)
            {
                Name = name;
            }

            /// <summary>The mode's name as written.</summary>
            public string Name { get; }

            /// <summary>What each of them says, and where it says it.</summary>
            public List<ModeAttributes> Declarations { get; } = new();
        }

        /// <summary>What one <c>xsl:mode</c> declaration writes, an attribute it leaves out being null.</summary>
        /// <param name="Precedence">The import precedence it was written at.</param>
        /// <param name="OnNoMatch">Its on-no-match, where written.</param>
        /// <param name="WarnOnNoMatch">Its warning-on-no-match, where written.</param>
        /// <param name="Written">The element, for the attributes read later.</param>
        private readonly record struct ModeAttributes(
            int Precedence,
            OnNoMatch? OnNoMatch,
            bool? WarnOnNoMatch,
            ModuleElement Written,
            string? OnMultipleMatch = null,
            string? Visibility = null,
            bool? Typed = null,
            bool StrictlyTyped = false);

        private int ResolveMode(int element, string? mode)
        {
            // Saying nothing, and saying "#default", both mean the default mode — which is the unnamed one
            // unless a default-mode in scope says otherwise.
            if (mode is null || mode == "#default")
            {
                return DefaultModeIn(element);
            }

            // Which is why 3.0 also has to have a way of naming the unnamed mode: once "#default" can be a
            // named mode, nothing else says "the one with no name".
            if (mode == "#unnamed" && Implements30)
            {
                return CompiledStylesheet.DefaultMode;
            }

            ExpandedName name = ResolveQualifiedName(element, mode);
            if (m_modes.TryGetValue(name, out int existing))
            {
                return existing;
            }

            int id = m_modes.Count;
            m_modes.Add(name, id);
            return id;
        }

        /// <summary>
        /// Reads <c>cdata-section-elements</c> into the settings, if the element carries it.
        /// </summary>
        /// <remarks>
        /// These name <em>elements</em>, so an unprefixed one takes the default namespace — unlike the QName
        /// of a template or a variable, where the default namespace deliberately does not apply. A stylesheet
        /// that declares a default namespace and then asks for <c>cdata-section-elements="note"</c> means the
        /// <c>note</c> it is producing, and reading it as no-namespace matched nothing at all.
        /// <para>
        /// This is the one output attribute that accumulates. Every other one is a choice, so a second
        /// <c>xsl:output</c> naming a different value either overrides the first or conflicts with it; this
        /// one is a list, and the specification says the several declarations amount to their union. A
        /// stylesheet importing a module that wraps one element and then wrapping another itself gets both.
        /// </para>
        /// </remarks>
        private void ReadCDataSectionElements(int element, OutputSettings settings)
        {
            if (OutputAttribute(element, "cdata-section-elements") is not string cdata)
            {
                return;
            }

            foreach (string token in cdata.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = token.IndexOf(':');
                string prefix = colon < 0 ? string.Empty : token[..colon];
                string local = colon < 0 ? token : token[(colon + 1)..];

                string uri = m_tree.ResolvePrefix(element, prefix)
                    ?? throw new XsltException($"Namespace prefix '{prefix}' in '{token}' is not bound.");

                if (!settings.CDataSectionElements.Contains((uri, local)))
                {
                    settings.CDataSectionElements.Add((uri, local));
                }
            }
        }

        /// <summary>
        /// Reads <c>suppress-indentation</c>, which names elements whose content is never indented.
        /// </summary>
        /// <remarks>
        /// A list and a union of every declaration of it, like <c>cdata-section-elements</c> and for the same
        /// reason. What it exists for is the elements where inserted whitespace is not cosmetic: text runs
        /// inside <c>p</c> and <c>pre</c> where a line break added for tidiness is a space the reader sees.
        /// </remarks>
        private void ReadSuppressIndentation(int element, OutputSettings settings)
        {
            if (OutputAttribute(element, "suppress-indentation") is not string suppressed)
            {
                return;
            }

            foreach (string token in suppressed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = token.IndexOf(':');
                string prefix = colon < 0 ? string.Empty : token[..colon];
                string local = colon < 0 ? token : token[(colon + 1)..];

                string uri = m_tree.ResolvePrefix(element, prefix)
                    ?? throw new XsltException($"Namespace prefix '{prefix}' in '{token}' is not bound.");

                if (!settings.SuppressIndentation.Contains((uri, local)))
                {
                    settings.SuppressIndentation.Add((uri, local));
                }
            }
        }

        /// <summary>The first child of an element that <c>use-when</c> has not taken out of the stylesheet.</summary>
        /// <param name="element">The parent.</param>
        private int FirstIncludedChild(int element) => SkipExcluded(m_tree.FirstChildOf(element));

        /// <summary>The next sibling that <c>use-when</c> has not taken out of the stylesheet.</summary>
        /// <param name="node">The node to move on from.</param>
        private int NextIncludedSibling(int node) => SkipExcluded(m_tree.NextSiblingOf(node));

        /// <summary>
        /// Whether an element has content, which is what makes writing both a <c>select</c> and content an
        /// error.
        /// </summary>
        /// <remarks>
        /// Whitespace-only text is not content: it is stripped from a stylesheet, so an instruction written
        /// across two lines has none. That has to be the same rule <see cref="CompileNode"/> follows, or an
        /// element would be refused for content it does not go on to produce.
        /// </remarks>
        /// <param name="element">The element to look inside.</param>
        private bool HasContent(int element)
        {
            for (int child = FirstIncludedChild(element); child >= 0; child = NextIncludedSibling(child))
            {
                switch (m_tree.KindOf(child))
                {
                    case NodeKind.Element:
                        return true;

                    case NodeKind.Text when
                        !m_tree.StringValueOf(child).AsSpan().Trim(" \t\r\n".AsSpan()).IsEmpty:
                        return true;
                }
            }

            return false;
        }

        private int SkipExcluded(int node)
        {
            while (node >= 0 && ExcludedByUseWhen(node))
            {
                node = m_tree.NextSiblingOf(node);
            }

            return node;
        }

        /// <summary>
        /// Whether <c>use-when</c> takes an element out of the stylesheet.
        /// </summary>
        /// <remarks>
        /// XSLT 2.0 §3.13: the expression is evaluated as the stylesheet is compiled, and a false answer
        /// removes the element and everything in it from the <em>stylesheet</em> — not from the result. What
        /// is inside is therefore never compiled and may be anything at all: syntax from a later version, an
        /// extension element this processor has never heard of, or an outright mistake. That is the point of
        /// the attribute, which exists so that one stylesheet can be written for several processors.
        /// </remarks>
        /// <param name="node">The node to test, which need not be an element.</param>
        private bool ExcludedByUseWhen(int node)
        {
            if (m_tree.KindOf(node) != NodeKind.Element)
            {
                return false;
            }

            // A declaration belonging to a later version of XSLT is not in the stylesheet either, and for a
            // reason of its own; see IgnoredAsALaterDeclaration. It is settled here because it has to be
            // settled first: the use-when below would otherwise be the one part of an ignored declaration
            // still able to refuse the stylesheet.
            if (IgnoredAsALaterDeclaration(node))
            {
                return true;
            }

            string? condition = IsXsltElement(node, out _)
                ? GetAttribute(node, "use-when")
                : GetXsltAttribute(node, "use-when");

            if (condition is null)
            {
                return false;
            }

            if (m_useWhen.TryGetValue((m_tree, node), out bool excluded))
            {
                return excluded;
            }

            excluded = !EvaluateUseWhen(node, condition);
            m_useWhen[(m_tree, node)] = excluded;
            return excluded;
        }

        /// <summary>
        /// Whether forwards-compatible processing ignores a declaration outright, before anything it carries
        /// is read.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 3.11: an element in the XSLT namespace standing among the declarations, which this
        /// version does not allow to stand there, is ignored together with its content. The stylesheet was
        /// written for a later version of XSLT, where the declaration means something; here it means nothing
        /// at all, so nothing in it is read and no part of it can refuse the stylesheet. That includes its
        /// use-when, which a stylesheet written for XSLT 4.0 may well have written in XPath 4.0 -- and which
        /// would be answered before the element's name was ever looked at.
        /// </remarks>
        /// <param name="element">The element to test, already known to be one.</param>
        private bool IgnoredAsALaterDeclaration(int element)
        {
            if (!IsForwardsCompatible(element) || !IsXsltElement(element, out string localName))
            {
                return false;
            }

            int parent = m_tree.ParentOf(element);

            if (parent < 0
                || m_tree.KindOf(parent) != NodeKind.Element
                || !IsXsltElement(parent, out string outermost)
                || outermost is not ("stylesheet" or "transform" or "package"))
            {
                return false;
            }

            XsltElement? shape = XsltElements.Find(localName);

            return shape is null || !IsAvailable(shape) || (shape.Placement & XsltPlacement.Declaration) == 0;
        }

        /// <summary>Answers one <c>use-when</c> expression.</summary>
        /// <param name="element">The element carrying it, whose prefixes and version it is read against.</param>
        /// <param name="condition">The expression as written.</param>
        private bool EvaluateUseWhen(int element, string condition)
        {
            int scope = m_scopeElement;
            m_scopeElement = element;
            m_inUseWhen = true;

            try
            {
                Expr expression = ParseExpression(element, condition);

                // The stylesheet's own tree stands in for a document, with no context item on it: there is
                // no source document here, and reading the context item is an error rather than a reading of
                // the stylesheet.
                DynamicContext context = new DynamicContext(
                    m_tree, DynamicContext.NotANode, m_names.BuildFingerprintMap(m_tree));
                context.DocumentLoader = LoadStaticDocument;
                context.Collations = m_options.CollationResolver;

                return expression.EvaluateAsBoolean(ref context);
            }
            finally
            {
                m_inUseWhen = false;
                m_scopeElement = scope;
            }
        }

        private ExpandedName ResolveQualifiedName(int element, string qualifiedName)
        {
            qualifiedName = qualifiedName.Trim();

            // XSLT 3.0 lets a name be written with its namespace in it rather than through a prefix, which
            // is what makes a stylesheet able to name something in a namespace it has not declared — and
            // what makes a generated stylesheet able to avoid inventing prefixes.
            if (qualifiedName.StartsWith("Q{", StringComparison.Ordinal) && Implements30)
            {
                int close = qualifiedName.IndexOf('}');

                if (close < 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"'{qualifiedName}' opens with 'Q{{' and never closes it.");
                }

                return new ExpandedName(qualifiedName[2..close], qualifiedName[(close + 1)..]);
            }

            int colon = qualifiedName.IndexOf(':');
            if (colon < 0)
            {
                return new ExpandedName(string.Empty, qualifiedName);
            }

            string prefix = qualifiedName[..colon];
            string? uri = m_tree.ResolvePrefix(element, prefix);
            if (uri is null)
            {
                throw new XsltException($"Namespace prefix '{prefix}' in '{qualifiedName}' is not bound.");
            }

            return new ExpandedName(uri, qualifiedName[(colon + 1)..]);
        }

        private Expr RequireExpression(int element, string attributeName)
        {
            string text = GetAttribute(element, attributeName)
                ?? throw new XsltException(
                    $"'xsl:{LocalNameOf(element)}' requires a '{attributeName}' attribute.");

            return ParseExpression(element, text);
        }

        private Expr ParseExpression(int element, string text)
        {
            m_scopeElement = element;
            Expr parsed = XPathParser.Parse(text, this);

            return m_backend == XsltBackend.Compiled
                ? Emit.ExpressionCompiler.Compile(parsed, text)
                : parsed;
        }

        private AttributeValueTemplate RequireAttributeValueTemplate(int element, string attributeName)
        {
            string text = GetAttribute(element, attributeName)
                ?? throw new XsltException(
                    $"'xsl:{LocalNameOf(element)}' requires a '{attributeName}' attribute.");

            m_scopeElement = element;
            return AttributeValueTemplate.Parse(text, this, m_backend);
        }

        private AttributeValueTemplate? OptionalAttributeValueTemplate(int element, string attributeName)
        {
            string? text = GetAttribute(element, attributeName);
            if (text is null)
            {
                return null;
            }

            m_scopeElement = element;
            return AttributeValueTemplate.Parse(text, this, m_backend);
        }

        private string? GetAttribute(int element, string localName)
        {
            // A shadow attribute stands in for the plain one where both are written (§3.13.2): the plain
            // one is ignored, whatever it says.
            if (m_hasShadowAttributes && ShadowAttribute(element, localName) is string shadowed)
            {
                return shadowed;
            }

            int fingerprint = m_tree.NameTable.LookupFingerprint(string.Empty, localName);
            if (fingerprint != NameTable.NoFingerprint)
            {
                int attribute = m_tree.FindAttribute(element, fingerprint);
                if (attribute >= 0)
                {
                    return m_tree.StringValueOf(attribute);
                }
            }

            return null;
        }

        /// <summary>
        /// Answers <c>doc()</c> in a static expression: the module itself for <c>doc('')</c>, which is how a
        /// stylesheet asks about its own attributes, and the document resolver for anything else.
        /// </summary>
        private XdmTree LoadStaticDocument(string href, string? baseUri)
        {
            // XSLT 2.0 §3.13.2 answers a static expression with the set of available documents empty, so
            // doc() finds nothing there and doc-available(), which asks by trying, answers false. 3.0
            // lifted that, and lifting it is what lets a shadow attribute read doc('').
            if (!Implements30)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FODC0002,
                    $"The document '{href}' cannot be read from a static expression: under XSLT 2.0 a "
                    + "use-when or a shadow attribute is answered with no documents available at all.");
            }

            if (href.Length == 0)
            {
                return m_tree;
            }

            ResolvedResource? resolved = m_options.DocumentResolver?.Resolve(href, baseUri ?? ModuleUriOf(m_tree));

            if (resolved is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FODC0002,
                    $"The document '{href}' could not be found for a static expression to read.");
            }

            using (resolved.Reader)
            {
                return XdmTreeBuilder.FromXml(resolved.Reader, entityResolver: m_options.EntityResolver, baseUri: resolved.Uri);
            }
        }

        /// <summary>
        /// Reads the shadow form of an attribute, <c>_name</c>, whose value is computed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XSLT 3.0 §3.9. An ordinary XSLT attribute is either fixed text or an attribute value template
        /// evaluated when the instruction runs; a shadow attribute is one the specification does not allow
        /// to be an AVT, computed <em>while the stylesheet is being compiled</em> from the static variables.
        /// So <c>_name</c> can decide a template's name, a mode, or a package's version — things that have
        /// to be settled before there is a transformation to compute anything in.
        /// </para>
        /// <para>
        /// Reached only when the stylesheet has an attribute starting with an underscore anywhere in it,
        /// which is a single scan of the name table per module. Every other stylesheet pays one field read
        /// on a miss, which is what keeps this off the compiler's hot path.
        /// </para>
        /// </remarks>
        private string? ShadowAttribute(int element, string localName)
        {
            int fingerprint = m_tree.NameTable.LookupFingerprint(string.Empty, "_" + localName);
            if (fingerprint == NameTable.NoFingerprint)
            {
                return null;
            }

            int attribute = m_tree.FindAttribute(element, fingerprint);
            if (attribute < 0 || !Implements30)
            {
                return null;
            }

            int scope = m_scopeElement;
            bool wasInUseWhen = m_inUseWhen;
            bool versionItself = localName == "version";

            m_scopeElement = element;
            m_inUseWhen = true;

            if (versionItself)
            {
                m_shadowVersionDepth++;
            }

            try
            {
                // Evaluated the way a use-when and a static variable are, and for the same reason: nothing
                // the stylesheet declares at run time exists yet.
                AttributeValueTemplate template = AttributeValueTemplate.Parse(
                    m_tree.StringValueOf(attribute), this, XsltBackend.Interpreted);

                DynamicContext context = new DynamicContext(
                    m_tree, DynamicContext.NotANode, m_names.BuildFingerprintMap(m_tree));
                context.DocumentLoader = LoadStaticDocument;
                context.Collations = m_options.CollationResolver;

                return template.Evaluate(ref context);
            }
            finally
            {
                if (versionItself)
                {
                    m_shadowVersionDepth--;
                }

                m_inUseWhen = wasInUseWhen;
                m_scopeElement = scope;
            }
        }

        private string? GetNamespacedAttribute(int element, string uri, string localName)
        {
            int fingerprint = m_tree.NameTable.LookupFingerprint(uri, localName);
            if (fingerprint == NameTable.NoFingerprint)
            {
                return null;
            }

            int attribute = m_tree.FindAttribute(element, fingerprint);
            return attribute < 0 ? null : m_tree.StringValueOf(attribute);
        }

        /// <summary>
        /// Reads an attribute that lives in the XSLT namespace. On a literal result element, processor
        /// directives such as <c>xsl:exclude-result-prefixes</c> are written with the prefix.
        /// </summary>
        private string? GetXsltAttribute(int element, string localName)
        {
            int fingerprint = m_tree.NameTable.LookupFingerprint(XsltNamespace, localName);
            if (fingerprint == NameTable.NoFingerprint)
            {
                return null;
            }

            int attribute = m_tree.FindAttribute(element, fingerprint);
            return attribute < 0 ? null : m_tree.StringValueOf(attribute);
        }

        private bool IsXsltElement(int element, out string localName)
        {
            int fingerprint = m_tree.FingerprintOf(element);
            localName = m_tree.NameTable.GetLocalName(fingerprint);
            return m_tree.NameTable.GetNamespaceUri(fingerprint) == XsltNamespace;
        }

        private string LocalNameOf(int element)
        {
            return m_tree.NameTable.GetLocalName(m_tree.FingerprintOf(element));
        }

        /// <summary>Gets an element's name as it was written, for reporting it back to the stylesheet author.</summary>
        private string QualifiedNameOf(int element)
        {
            string prefix = m_tree.NameTable.GetPrefix(m_tree.NameCodeOf(element));
            string localName = LocalNameOf(element);

            return prefix.Length == 0 ? localName : $"{prefix}:{localName}";
        }

        /// <summary>
        /// Refuses the schema-validation attributes, which this engine cannot honour.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A processor that cannot validate has to refuse rather than ignore. A stylesheet asking for strict
        /// validation is asking to be told when its result does not fit the schema, and quietly handing back
        /// an unvalidated result answers a question it did not ask.
        /// </para>
        /// <para>
        /// Which of the four is refused is the processor's version to say, and the two languages drew the
        /// line in different places. XSLT 2.0 refuses everything but <c>strip</c> from a basic processor;
        /// 3.0 refuses only <c>strict</c>, having noticed that <c>preserve</c> and <c>lax</c> ask for
        /// nothing a processor without a schema cannot give — with no type annotations anywhere, preserving
        /// them and stripping them come to the same thing, and validating laxly against no declaration at
        /// all validates nothing. So the line moves with the version this engine says it implements, as the
        /// rest of the vocabulary does.
        /// </para>
        /// </remarks>
        /// <param name="element">The element that may carry them.</param>
        /// <param name="xslt">
        /// Whether to look for the attributes in the XSLT namespace, which is where a literal result element
        /// carries them — an unprefixed <c>type</c> there is an attribute of the result, not an instruction
        /// to this engine.
        /// </param>
        /// <summary>
        /// Refuses a <c>default-validation</c> asking for validation this engine cannot do.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="RejectSchemaValidation"/> because the grammar is narrower here — the
        /// attribute takes only <c>preserve</c> and <c>strip</c> — so <c>strict</c> is both outside the
        /// grammar and a request for schema awareness. The specification names the second of those, and
        /// naming the first instead would tell a stylesheet its spelling was wrong when its spelling was
        /// fine and this processor simply cannot do what it asked.
        /// </remarks>
        /// <param name="element">The element that may carry the attribute.</param>
        private void RejectSchemaDefaultValidation(int element)
        {
            // A schema-aware processor honours default-validation; a value outside its grammar
            // (strict, lax) is caught as XTSE0020 by the attribute-value check, not here.
            if (m_schemas is not null)
            {
                return;
            }

            if (GetAttribute(element, "default-validation") is not string validation)
            {
                return;
            }

            if (RefusesValidation(validation))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE1660,
                    $"{QualifiedNameOf(element)} asks for '{validation}' as the default validation, which "
                    + "needs a schema-aware processor. This engine does not validate.");
            }
        }

        /// <summary>
        /// Wraps a constructor instruction so that it validates its result, where the stylesheet asks: an
        /// explicit <c>validation</c> or <c>type</c>, or the <c>default-validation</c> in scope. Returns
        /// the instruction unchanged where nothing is to be validated, and refuses validation outright where
        /// the processor is not schema-aware.
        /// </summary>
        /// <param name="element">The instruction element.</param>
        /// <param name="inner">The instruction it compiled to.</param>
        /// <param name="shape">What the instruction produces.</param>
        /// <param name="literal">Whether the attributes are in the XSLT namespace, as on a literal result element.</param>
        private Instruction WithValidation(int element, Instruction inner, ValidationShape shape, bool literal = false)
        {
            if (m_schemas is null)
            {
                RejectSchemaValidation(element, literal);
                return inner;
            }

            (string mode, XdmSchemaType? type) = EffectiveValidation(element, literal);

            // strip and preserve validate nothing; a freshly constructed element and its content are
            // untyped whichever is asked, so the instruction stands as it is.
            if (type is null && mode is not ("strict" or "lax"))
            {
                return inner;
            }

            return new ValidatingInstruction(
                inner, shape, mode == "strict", type, StaticBaseUri(element), NamespacesOn(element));
        }

        /// <summary>
        /// The validation a constructor is subject to: a <c>type</c> it names, or a mode — the explicit
        /// <c>validation</c>, or the <c>default-validation</c> in scope, or <c>strip</c>.
        /// </summary>
        /// <exception cref="XsltException">
        /// <c>XTSE1505</c> for both a type and a validation; <c>XTSE1520</c> for a type that is not in scope.
        /// </exception>
        private (string Mode, XdmSchemaType? Type) EffectiveValidation(int element, bool literal)
        {
            string? validation = literal ? GetXsltAttribute(element, "validation") : GetAttribute(element, "validation");
            string? type = literal ? GetXsltAttribute(element, "type") : GetAttribute(element, "type");

            if (validation is not null && type is not null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE1505,
                    $"{QualifiedNameOf(element)} has both a type and a validation attribute, and a node is "
                    + "validated by one or the other.");
            }

            if (type is not null)
            {
                return (string.Empty, ResolveNamedType(element, type));
            }

            if (validation is not null)
            {
                return (validation, null);
            }

            return (DefaultValidationInScope(element), null);
        }

        /// <summary>The type a <c>type</c> attribute names, among the schema components in scope.</summary>
        /// <exception cref="XsltException"><c>XTSE1520</c> where the name is not a QName or names no type in scope.</exception>
        private XdmSchemaType ResolveNamedType(int element, string type)
        {
            ExpandedName name;

            try
            {
                name = ResolveQualifiedName(element, type);
            }
            catch (XsltException)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE1520,
                    $"The type attribute of {QualifiedNameOf(element)} is '{type}', which is not a QName or "
                    + "uses a prefix nothing in scope binds.");
            }

            // An unprefixed type name is in the default namespace that is in scope, as an unprefixed
            // element name is (XSLT 3.0 §5.6.1): xpath-default-namespace, or none.
            if (name.NamespaceUri.Length == 0
                && !type.Trim().StartsWith("Q{", StringComparison.Ordinal)
                && type.IndexOf(':') < 0
                && DefaultElementNamespaceAt(element) is { Length: > 0 } defaultNamespace)
            {
                name = new ExpandedName(defaultNamespace, name.LocalName);
            }

            return m_schemas!.FindType(name.NamespaceUri, name.LocalName)
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XTSE1520,
                    $"The type attribute of {QualifiedNameOf(element)} is '{type}', which names no type among "
                    + "the schema components in scope.");
        }

        /// <summary>
        /// Whether an <c>xsl:copy</c> or <c>xsl:copy-of</c> keeps the type annotations of what it copies:
        /// only where its effective validation is <c>preserve</c>. Strict, lax and a type validate afresh —
        /// the wrapper puts back what validation settles — and strip and the default drop annotations.
        /// </summary>
        private bool PreservesTypesOnCopy(int element)
        {
            if (m_schemas is null)
            {
                // No schema, so nothing carries an annotation; the flag is a no-op, kept true for the
                // path that copies an already-typed tree the caller supplied through the input.
                return true;
            }

            (string mode, XdmSchemaType? type) = EffectiveValidation(element, literal: false);
            return type is null && mode == "preserve";
        }

        /// <summary>The nearest <c>default-validation</c> above an element, or <c>strip</c> where none is.</summary>
        private string DefaultValidationInScope(int element)
        {
            for (int current = element; current >= 0; current = m_tree.ParentOf(current))
            {
                if (m_tree.KindOf(current) == NodeKind.Element
                    && IsXsltElement(current, out _)
                    && GetAttribute(current, "default-validation") is string said)
                {
                    return said.Trim();
                }
            }

            return "strip";
        }

        private void RejectSchemaValidation(int element, bool xslt = false)
        {
            // A schema-aware processor honours validation and type; what it does with them is settled where
            // the instruction is compiled, by WithValidation. Here there is nothing to refuse.
            if (m_schemas is not null)
            {
                return;
            }

            string? validation = xslt
                ? GetXsltAttribute(element, "validation")
                : GetAttribute(element, "validation");

            string? type = xslt ? GetXsltAttribute(element, "type") : GetAttribute(element, "type");
            string written = xslt ? "xsl:validation" : "validation";

            if (validation is not null)
            {
                if (validation is not ("strict" or "lax" or "preserve" or "strip"))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0020,
                        $"The {written} attribute of {QualifiedNameOf(element)} is '{validation}', "
                        + "which is not one of strict, lax, preserve or strip.");
                }

                if (RefusesValidation(validation))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE1660,
                        $"{QualifiedNameOf(element)} asks for '{validation}' validation, which needs a "
                        + (m_schemas is null
                            ? "schema-aware processor. This engine does not validate."
                            : "processor that validates what it constructs, which this one does not yet."));
                }
            }

            if (type is not null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE1660,
                    $"{QualifiedNameOf(element)} names a type, which needs a "
                    + (m_schemas is null
                        ? "schema-aware processor. This engine does not validate."
                        : "processor that validates what it constructs, which this one does not yet."));
            }
        }

        /// <summary>
        /// Whether a validation mode is one this engine has to refuse.
        /// </summary>
        /// <remarks>
        /// <c>XTSE1660</c> names the set, and the two languages name different sets: a 2.0 basic processor
        /// refuses every value but <c>strip</c>, where a 3.0 non-schema-aware processor refuses only
        /// <c>strict</c> — <c>preserve</c> and <c>lax</c> having been let through once it was clear they ask
        /// for nothing a processor without a schema cannot give.
        /// </remarks>
        /// <param name="validation">The value written, which the grammar has already been checked against.</param>
        private bool RefusesValidation(string validation)
        {
            return Implements30 ? validation == "strict" : validation != "strip";
        }

        /// <summary>
        /// Compiles the <c>select</c> that XSLT 2.0 lets stand in for an instruction's content.
        /// </summary>
        /// <remarks>
        /// Writing both is refused rather than one quietly winning: the value comes from one or the other, and
        /// a stylesheet that says both has not decided which.
        /// </remarks>
        /// <summary>
        /// Reads a <c>select</c> that stands in for the element's content, refusing both together.
        /// </summary>
        /// <remarks>
        /// The specification gives each of these instructions its own code for the same mistake, so the code
        /// is chosen by which element is being compiled rather than shared. A test asking for
        /// <c>XTSE0870</c> is asking about <c>xsl:value-of</c> in particular.
        /// </remarks>
        private Expr? CompileValueSelect(int element)
        {
            string? select = GetAttribute(element, "select");
            if (select is null)
            {
                return null;
            }

            if (HasContent(element))
            {
                throw XsltErrors.Error(
                    LocalNameOf(element) switch
                    {
                        "value-of" => XsltErrorCode.XTSE0870,
                        "attribute" => XsltErrorCode.XTSE0840,
                        "comment" => XsltErrorCode.XTSE0940,
                        "processing-instruction" => XsltErrorCode.XTSE0880,
                        "namespace" => XsltErrorCode.XTSE0910,
                        "map-entry" => XsltErrorCode.XTSE3280,
                        _ => XsltErrorCode.XTSE0620,
                    },
                    $"{QualifiedNameOf(element)} has both a select attribute and content. Its value comes "
                    + "from one or the other.");
            }

            return ParseExpression(element, select);
        }

        /// <summary>Refuses a written <c>collation</c> naming a collation nobody has.</summary>
        /// <remarks>
        /// What the attribute says, where it says it outright. An attribute value template says nothing
        /// until it is evaluated, so an instruction that allows one resolves it for itself when it runs.
        /// </remarks>
        /// <param name="element">The element carrying the <c>collation</c>.</param>
        /// <param name="code">The code XSLT gives this complaint on that element.</param>
        private void RequireKnownCollation(int element, XsltErrorCode code)
        {
            if (GetAttribute(element, "collation") is string collation)
            {
                ResolveKnownCollation(collation.Trim(), code);
            }
        }

        /// <summary>
        /// The collation a URI names, among the engine's and the caller's, or the code an element gives
        /// for naming one nobody has.
        /// </summary>
        /// <remarks>
        /// One refusal, a code apiece: an unusable collation is <c>XTDE1035</c> on <c>xsl:sort</c>,
        /// <c>XTDE1110</c> on <c>xsl:for-each-group</c> and <c>XTSE1210</c> on <c>xsl:key</c>, so the
        /// element names it rather than every one of them hearing the function library's <c>FOCH0002</c>.
        /// </remarks>
        /// <param name="uri">The collation URI, trimmed.</param>
        /// <param name="code">The code XSLT gives this complaint on the element asking.</param>
        private Collation ResolveKnownCollation(string uri, XsltErrorCode code)
        {
            try
            {
                return Collation.Resolve(uri, m_options.CollationResolver);
            }
            catch (XsltException failed)
            {
                throw XsltErrors.Error(
                    code, $"'{uri}' is not a collation this processor has: {failed.Message}", failed);
            }
        }

        /// <summary>
        /// Refuses a <c>doctype-public</c> that is not a public identifier.
        /// </summary>
        /// <remarks>
        /// XML fixes the characters a public identifier may hold, and neither identifier of a document type
        /// declaration may be escaped — so one holding anything else cannot be written at all, and saying so
        /// where the stylesheet says it beats producing a document nothing can parse.
        /// </remarks>
        /// <param name="identifier">The value written on the attribute.</param>
        private static void RequirePublicIdentifier(string identifier)
        {
            const string Punctuation = "-'()+,./:=?;!*#@$_% \r\n";

            foreach (char character in identifier)
            {
                if (char.IsAsciiLetterOrDigit(character) || Punctuation.Contains(character))
                {
                    continue;
                }

                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"'{identifier}' cannot be a doctype-public: '{character}' is not one of the characters "
                    + "XML allows in a public identifier, which are the ASCII letters and digits and "
                    + $"{Punctuation.Trim()}.");
            }
        }

        /// <summary>
        /// Reads the <c>normalization-form</c> of <c>xsl:output</c>.
        /// </summary>
        /// <remarks>
        /// <c>fully-normalized</c> is refused rather than approximated: it is not one of the four forms
        /// .NET provides, and normalizing to a different form than the one asked for would be worse than
        /// saying so.
        /// </remarks>
        private static System.Text.NormalizationForm? ReadNormalizationForm(string form)
        {
            return form.Trim() switch
            {
                "none" => null,
                "NFC" => System.Text.NormalizationForm.FormC,
                "NFD" => System.Text.NormalizationForm.FormD,
                "NFKC" => System.Text.NormalizationForm.FormKC,
                "NFKD" => System.Text.NormalizationForm.FormKD,
                string other => throw new XsltException(
                    $"'{other}' is not a normalization form this engine applies. It has NFC, NFD, NFKC, NFKD "
                    + "and none."),
            };
        }

        /// <summary>Reads <c>copy-namespaces</c>, which defaults to copying them.</summary>
        /// <summary>
        /// Reads <c>inherit-namespaces</c>, which is yes unless written: whether the element being built
        /// passes its namespaces on to the children built inside it.
        /// </summary>
        /// <param name="element">The instruction, or the literal result element.</param>
        /// <param name="xslt">Whether the attribute is the <c>xsl:inherit-namespaces</c> of a literal result element.</param>
        private bool ReadInheritNamespaces(int element, bool xslt = false)
        {
            string? said = xslt
                ? GetXsltAttribute(element, "inherit-namespaces")
                : GetAttribute(element, "inherit-namespaces");

            return said is null || said.Trim() is "yes" or "true" or "1";
        }

        private bool ReadCopyNamespaces(int element)
        {
            return GetAttribute(element, "copy-namespaces") is null
                || ReadDeclarationFlag(element, "copy-namespaces");
        }

        /// <remarks>
        /// Trimmed first: these attributes are token-typed, so <c>" yes "</c> is <c>yes</c> with whitespace
        /// around it and not a third value that is neither.
        /// </remarks>
        private static bool IsYes(string? value)
        {
            // All six spellings XSLT 3.0 widened every boolean attribute to. A 2.0 stylesheet writing "true"
            // is refused by the element table before it reaches here, so accepting them all in one place
            // does not let the extra spellings through at 2.0.
            return value?.Trim() is "yes" or "true" or "1";
        }

        /// <summary>
        /// Reads a yes-or-no attribute that decides what a declaration means: <c>tunnel</c> and
        /// <c>required</c> on a parameter.
        /// </summary>
        /// <remarks>
        /// Stricter than <see cref="IsYes"/>, which is used where a misread value only changes formatting.
        /// Here a misspelling would leave the stylesheet running and quietly doing something else — binding a
        /// parameter from the wrong place, or falling back to a default that ought not to exist — so anything
        /// but a boolean is refused. The words beyond <c>yes</c> and <c>no</c> are what XSLT 3.0 added;
        /// accepting them costs nothing and spares a stylesheet written against the later spec a puzzling
        /// failure.
        /// </remarks>
        /// <param name="element">The element carrying the attribute.</param>
        /// <param name="attributeName">The attribute to read.</param>
        /// <summary>
        /// Reads <c>new-each-time</c>, whose third value is the reason it is not an ordinary boolean.
        /// </summary>
        /// <returns>
        /// True where the function must build afresh on every call, false where it must not, and
        /// <see langword="null"/> for <c>maybe</c> — and for the attribute being absent, <c>maybe</c> being
        /// what the specification defaults it to.
        /// </returns>
        private bool? ReadNewEachTime(int element)
        {
            string? value = GetAttribute(element, "new-each-time")?.Trim();

            return value switch
            {
                null or "maybe" => null,
                "no" or "false" or "0" => false,
                "yes" or "true" or "1" => true,
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"The new-each-time attribute of {QualifiedNameOf(element)} is '{value}', which is none "
                    + "of 'yes', 'no' and 'maybe'."),
            };
        }

        private bool ReadDeclarationFlag(int element, string attributeName)
        {
            string? value = GetAttribute(element, attributeName)?.Trim();

            return value switch
            {
                null or "no" or "false" or "0" => false,
                "yes" or "true" or "1" => true,
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XTSE0020,
                    $"The {attributeName} attribute of {QualifiedNameOf(element)} is '{value}', which is "
                    + "neither 'yes' nor 'no'."),
            };
        }

        private readonly record struct VariableBinding(ExpandedName Name, int Slot, bool IsGlobal);
    }
}
