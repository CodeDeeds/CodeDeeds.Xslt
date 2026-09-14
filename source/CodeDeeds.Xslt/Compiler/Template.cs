using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>A declared parameter of a template, with the expression producing its default value.</summary>
    internal sealed class TemplateParameter
    {
        /// <summary>Initializes a parameter.</summary>
        public TemplateParameter(
            ExpandedName name,
            int slot,
            Expr? select,
            Instruction[]? body,
            bool tunnel,
            bool required,
            XdmSequenceType? type = null)
        {
            Name = name;
            Slot = slot;
            Select = select;
            Body = body;
            Tunnel = tunnel;
            Required = required;
            Type = type;
        }

        /// <summary>The parameter's expanded name, used to match <c>xsl:with-param</c>.</summary>
        public ExpandedName Name { get; }

        /// <summary>The slot in the template's frame that holds the value.</summary>
        public int Slot { get; }

        /// <summary>The default value expression, if the parameter was written with a <c>select</c>.</summary>
        public Expr? Select { get; }

        /// <summary>The default value content, if the parameter was written with a body.</summary>
        public Instruction[]? Body { get; }

        /// <summary>
        /// Whether the parameter is declared <c>tunnel="yes"</c>, and so is taken from the tunnel set rather
        /// than from the call site.
        /// </summary>
        /// <remarks>
        /// The declaration says only that this template wants to read the value. It does not affect what is
        /// tunnelled onward: a template passes the whole tunnel set on whether or not it declares any of it,
        /// which is the entire point of the feature.
        /// </remarks>
        public bool Tunnel { get; }

        /// <summary>
        /// Whether the caller must supply a value, rather than the parameter having a default to fall back on.
        /// </summary>
        /// <remarks>
        /// A required parameter has no default — declaring one would be a contradiction, and is refused — so
        /// there is nothing to fall back to and an invocation that omits it is an error.
        /// </remarks>
        public bool Required { get; }

        /// <summary>The base URI of the declaration, which a tree built from its content takes.</summary>
        public string? BaseUri { get; init; }

        /// <summary>The type declared by an <c>as</c> attribute, which every value bound here must fit.</summary>
        public XdmSequenceType? Type { get; }
    }

    /// <summary>A compiled <c>xsl:template</c>.</summary>
    internal sealed class Template
    {
        /// <summary>Initializes a template.</summary>
        public Template(int index, Pattern[] patterns, ExpandedName? name, double? explicitPriority)
        {
            Index = index;
            Patterns = patterns;
            Name = name;
            ExplicitPriority = explicitPriority;
        }

        /// <summary>The template's position in the stylesheet, which breaks ties between equal priorities.</summary>
        public int Index { get; }

        /// <summary>The patterns this template matches, empty for a template that can only be called by name.</summary>
        /// <remarks>
        /// Set once the whole stylesheet has been read rather than where the declaration is: a pattern may
        /// call a function the stylesheet declares further down, and a stylesheet's declarations are not
        /// written in any order that a reader is entitled to depend on.
        /// </remarks>
        public Pattern[] Patterns { get; set; }

        /// <summary>The template's name, if it has one.</summary>
        public ExpandedName? Name { get; }

        /// <summary>The priority written on the template, overriding the pattern's default.</summary>
        public double? ExplicitPriority { get; }

        /// <summary>
        /// The precedence of the module this template came from; higher wins.
        /// </summary>
        /// <remarks>
        /// Import precedence outranks priority entirely: a template in the importing stylesheet beats an
        /// imported one however specific the imported pattern is. That is what lets a stylesheet import
        /// another and override just the parts it wants to change.
        /// </remarks>
        public int ImportPrecedence { get; init; }

        /// <summary>
        /// The lowest precedence the module this template came from imported, which with
        /// <see cref="ImportPrecedence"/> bounds what an <c>xsl:apply-imports</c> in its body reaches.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A rule overrides the rules its own module imported, and those alone: XSLT says
        /// <c>xsl:apply-imports</c> processes the node with the rules that were imported into the module
        /// containing the rule, which is not the same as every rule of lower precedence. A module
        /// importing two others gives them both a lower precedence than its own, and neither of them has
        /// imported the other, so an <c>xsl:apply-imports</c> in one must not reach the other.
        /// </para>
        /// <para>
        /// A range of precedences says it because precedences are handed out depth-first: what a module
        /// imported has the numbers between this and its own. Zero for a module that imported nothing,
        /// where the range is empty and an <c>xsl:apply-imports</c> falls straight to the built-in rule.
        /// </para>
        /// </remarks>
        public int ImportFloor { get; init; }

        /// <summary>The number of variable slots the template's frame requires.</summary>
        public int FrameSize { get; set; }

        /// <summary>The template's declared parameters.</summary>
        public TemplateParameter[] Parameters { get; set; } = Array.Empty<TemplateParameter>();

        /// <summary>The instructions forming the template's body.</summary>
        public Instruction[] Body { get; set; } = Array.Empty<Instruction>();

        /// <summary>What <c>xsl:context-item</c> says this template expects to be standing on.</summary>
        public ContextItemDeclaration ContextItem { get; set; } = ContextItemDeclaration.Default;

        /// <summary>The template this one replaced through an xsl:override, which xsl:original names.</summary>
        public Template? Original { get; set; }

        /// <summary>The type the template declares for its result, where it declares one.</summary>
        public XdmSequenceType? ResultType { get; set; }

        /// <summary>How far outside its own package this template can be seen.</summary>
        public Visibility Visibility { get; set; } = Visibility.Private;

        /// <summary>
        /// What the principal package holds this template as, where it came from a package that one
        /// uses; null where it was declared in the principal package itself and there is no boundary
        /// for it to have crossed.
        /// </summary>
        /// <remarks>
        /// A template declares its visibility to the package that wrote it, and a package using that
        /// one takes it as whatever its <c>xsl:accept</c> asked for, or as private where it asked for
        /// nothing: using a package does not re-offer what that package offers. So what a library
        /// declares public is not an entry point of the package using it unless that package said so,
        /// and the visibility written on the declaration is the wrong question to put to it.
        /// </remarks>
        public Visibility? VisibleInPrincipal { get; set; }
    }

    /// <summary>
    /// How far outside its own package a component can be seen.
    /// </summary>
    /// <remarks>
    /// XSLT 3.0 §3.5. The default is <see cref="Private"/> and that is the point of the feature: a package
    /// says what it offers, and everything it does not say is its own business. A stylesheet that is not a
    /// package has no boundary for any of this to be about, so its components behave as public.
    /// </remarks>
    internal enum Visibility : byte
    {
        /// <summary>Visible only inside the package that declares it, which is the default.</summary>
        Private,

        /// <summary>Offered to using packages, which may also override it.</summary>
        Public,

        /// <summary>Offered to using packages, which may not override it.</summary>
        Final,

        /// <summary>Named but not defined; a using package has to supply it.</summary>
        Abstract,

        /// <summary>Reached only through <c>xsl:original</c>, from the override that replaced it.</summary>
        Hidden,

        /// <summary>
        /// An abstract component a using package took without supplying: still there to be named, an
        /// error to reach.
        /// </summary>
        /// <remarks>
        /// The word is the specification's, and the distinction is the suite's. A component hidden outright
        /// is invisible from the package that hid it, so naming it is a static error; an abstract one
        /// accepted as absent — or accepted with nothing said, which comes to the same — can be named, in
        /// the package that took it and in the library that declared it, and fails only when something
        /// reaches it, as <c>XTDE3052</c>.
        /// </remarks>
        Absent,
    }

    /// <summary>Whether a context item is wanted where a template's body runs.</summary>
    internal enum ContextItemUse : byte
    {
        /// <summary>One may be there or not, which is what a template says by saying nothing.</summary>
        Optional,

        /// <summary>One must be there, and calling without one is an error.</summary>
        Required,

        /// <summary>There must not be one, and the body runs with no focus even if the caller had one.</summary>
        Absent,
    }

    /// <summary>
    /// What <c>xsl:context-item</c> declares about the item a template is standing on.
    /// </summary>
    /// <remarks>
    /// XSLT 3.0 §9.7, and what it is for is written into the shape of it: a template that says
    /// <c>use="absent"</c> is a template that cannot read its surroundings, which is a promise to the reader
    /// and to the processor alike. The declared type is checked at the call rather than trusted, so a
    /// template asking for <c>xs:integer</c> gets one or gets an error naming the code.
    /// </remarks>
    /// <param name="Use">Whether an item is wanted, required, or refused.</param>
    /// <param name="Type">The type it must match, or null where any will do.</param>
    internal readonly record struct ContextItemDeclaration(ContextItemUse Use, XdmSequenceType? Type)
    {
        /// <summary>What a template that declares nothing expects, which is anything or nothing.</summary>
        public static ContextItemDeclaration Default => new ContextItemDeclaration(ContextItemUse.Optional, null);

        /// <summary>Whether this says anything the invocation has to act on.</summary>
        public bool Says => Use != ContextItemUse.Optional || Type is not null;
    }

    /// <summary>One pattern of one template in one mode, paired with the priority that pattern confers.</summary>
    /// <remarks>
    /// The mode belongs to the rule rather than to the template because XSLT 2.0 lets one template serve
    /// several modes, or every mode at once. Each is a separate way of reaching the same body.
    /// </remarks>
    internal readonly struct TemplateRule
    {
        /// <summary>Initializes a rule.</summary>
        public TemplateRule(Pattern pattern, Template template, double priority, int mode)
        {
            Pattern = pattern;
            Template = template;
            Priority = priority;
            Mode = mode;
        }

        /// <summary>The mode this rule belongs to; see <see cref="CompiledStylesheet.DefaultMode"/>.</summary>
        public int Mode { get; }

        /// <summary>The pattern to test.</summary>
        public Pattern Pattern { get; }

        /// <summary>The template to run when the pattern matches.</summary>
        public Template Template { get; }

        /// <summary>The effective priority, explicit if written and otherwise derived from the pattern.</summary>
        public double Priority { get; }
    }

    /// <summary>
    /// Chooses which template to apply to a node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Testing every pattern against every node would make dispatch the dominant cost of a transformation.
    /// Instead rules whose innermost step is a name test are bucketed by mode, node kind and name, so a node
    /// only ever sees rules that could plausibly match it. Rules with a wildcard or kind test cannot be
    /// bucketed by name and are held in a per-mode list that is merged in during lookup.
    /// </para>
    /// <para>
    /// The index is built per transformation because a name test resolves to a fingerprint only once the input
    /// tree's name table is known; see <see cref="NameSlotTable"/>. Building it costs one pass over the rules.
    /// </para>
    /// </remarks>
    internal sealed class TemplateIndex
    {
        private readonly Dictionary<long, List<TemplateRule>> m_byName = new();
        private readonly Dictionary<int, List<TemplateRule>> m_byMode = new();

        /// <summary>Builds an index for one input tree.</summary>
        /// <param name="rules">Every rule in the stylesheet.</param>
        /// <param name="fingerprintMap">Slot-to-fingerprint mapping for the tree being transformed.</param>
        public TemplateIndex(IReadOnlyList<TemplateRule> rules, int[] fingerprintMap)
        {
            foreach (TemplateRule rule in rules)
            {
                int slot = rule.Pattern.NameSlot;
                NodeKind? kind = rule.Pattern.RequiredKind;

                if (slot >= 0 && kind.HasValue && fingerprintMap[slot] != NameTable.NoFingerprint)
                {
                    long key = MakeKey(rule.Mode, kind.Value, fingerprintMap[slot]);
                    Add(m_byName, key, rule);
                    continue;
                }

                if (slot >= 0)
                {
                    // The name never occurs in this document, so the rule cannot match anything here.
                    continue;
                }

                Add(m_byMode, rule.Mode, rule);
            }

            foreach (List<TemplateRule> bucket in m_byName.Values)
            {
                Sort(bucket);
            }

            foreach (List<TemplateRule> bucket in m_byMode.Values)
            {
                Sort(bucket);
            }
        }

        /// <summary>
        /// Finds the template that applies to a node, or <see langword="null"/> if none does and the built-in
        /// rule should be used instead.
        /// </summary>
        /// <param name="node">The node being matched.</param>
        /// <param name="mode">The mode in force.</param>
        /// <param name="context">The context used to evaluate pattern predicates.</param>
        /// <summary>
        /// Finds the template that applies to a node, considering only modules below a given precedence.
        /// </summary>
        /// <remarks>
        /// The ceiling is what implements <c>xsl:apply-imports</c>: it asks for the template that would have
        /// matched had the current template's module — and everything importing it — not existed.
        /// </remarks>
        /// <param name="node">The node being matched.</param>
        /// <param name="mode">The mode in force.</param>
        /// <param name="context">The context used to evaluate pattern predicates.</param>
        /// <param name="maximumPrecedence">Only templates strictly below this precedence are considered.</param>
        /// <summary>
        /// Finds the template that would match were everything up to and including one already-tried template
        /// not there, which is what <c>xsl:next-match</c> asks for.
        /// </summary>
        /// <remarks>
        /// Different from passing a maximum precedence, which is what <c>xsl:apply-imports</c> does. That
        /// skips a whole module; this skips exactly the templates already considered, so the next one by
        /// priority in the same module is reachable.
        /// </remarks>
        /// <param name="node">The node to match.</param>
        /// <param name="mode">The mode in force.</param>
        /// <param name="context">The context, positioned on the node.</param>
        /// <param name="after">The rule already running, which and whose betters are passed over.</param>
        public TemplateRule? FindAfter(int node, int mode, ref DynamicContext context, TemplateRule after)
        {
            return FindRule(node, mode, ref context, int.MaxValue, after);
        }

        /// <summary>
        /// Finds the template that applies to an item that is not a node.
        /// </summary>
        /// <remarks>
        /// Only the per-mode list is consulted, and only its predicate patterns: everything bucketed by name
        /// is bucketed by node kind too, and an atomic value has neither. The list is already in descending
        /// priority, so the first that matches is the one.
        /// </remarks>
        /// <param name="item">The item being matched.</param>
        /// <param name="mode">The mode in force.</param>
        /// <param name="context">The context used to evaluate the predicates.</param>
        /// <param name="item">The item being matched.</param>
        /// <param name="mode">The mode in force.</param>
        /// <param name="context">The context used to evaluate the predicates.</param>
        /// <param name="after">
        /// The rule already running, which and whose betters are passed over — what <c>xsl:next-match</c>
        /// asks for. Null to search from the top.
        /// </param>
        /// <param name="maximumPrecedence">
        /// Only rules strictly below this precedence are considered — what <c>xsl:apply-imports</c> asks for.
        /// </param>
        /// <remarks>
        /// The two extra arguments are here for the same reason they are on <see cref="Find"/>: an atomic
        /// value can be the context item of a rule, so that rule can contain an <c>xsl:next-match</c> or an
        /// <c>xsl:apply-imports</c>, and sending either through the node-keyed search asks the tree for the
        /// kind of a node that is not there.
        /// </remarks>
        public TemplateRule? FindForItem(
            XPathValue item,
            int mode,
            ref DynamicContext context,
            TemplateRule? after = null,
            int maximumPrecedence = int.MaxValue,
            int minimumPrecedence = int.MinValue)
        {
            if (!m_byMode.TryGetValue(mode, out List<TemplateRule>? generic))
            {
                return null;
            }

            bool skipping = after is not null;
            bool passed = false;

            foreach (TemplateRule rule in generic)
            {
                if (rule.Template.ImportPrecedence >= maximumPrecedence
                    || rule.Template.ImportPrecedence < minimumPrecedence)
                {
                    continue;
                }

                if (skipping && !Reached(rule, after!.Value, ref passed))
                {
                    continue;
                }

                skipping = false;

                if (rule.Pattern.ItemPredicates is not null && rule.Pattern.MatchesItem(item, ref context))
                {
                    return rule;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether a rule comes after the one already running, so that the search proper can start at it.
        /// </summary>
        /// <remarks>
        /// The rules are walked in the order they would be chosen in, so everything before the running rule
        /// has been tried already and everything after it is a candidate. What counts as "the running rule"
        /// is one pattern and not one template: a template whose <c>match</c> is a union is several rules,
        /// one per alternative, each with the default priority of its own alternative (XSLT 3.0 §6.4) — so
        /// <c>xsl:next-match</c> can go from one alternative to the next. Unless the template wrote a
        /// priority of its own, which leaves nothing to tell the alternatives apart and makes them one rule
        /// again; there they are passed over together.
        /// </remarks>
        /// <param name="rule">The rule the walk has reached.</param>
        /// <param name="after">The rule already running.</param>
        /// <param name="passed">Whether the running rule has been walked past, updated here.</param>
        private static bool Reached(TemplateRule rule, TemplateRule after, ref bool passed)
        {
            bool ofTheSameTemplate = ReferenceEquals(rule.Template, after.Template);

            if (!passed)
            {
                passed = ofTheSameTemplate
                    && (after.Template.ExplicitPriority is not null
                        || ReferenceEquals(rule.Pattern, after.Pattern));

                return false;
            }

            return after.Template.ExplicitPriority is null || !ofTheSameTemplate;
        }

        public TemplateRule? Find(
            int node,
            int mode,
            ref DynamicContext context,
            int maximumPrecedence,
            int minimumPrecedence = int.MinValue)
        {
            return FindRule(node, mode, ref context, maximumPrecedence, after: null, minimumPrecedence);
        }

        /// <summary>
        /// Whether another rule of the same import precedence and priority as the one chosen also matches
        /// the node, which a mode declared <c>on-multiple-match="fail"</c> refuses.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="mode">The mode.</param>
        /// <param name="context">The context.</param>
        /// <param name="chosen">The template found for the node.</param>
        public bool HasRivalOfEqualRank(int node, int mode, ref DynamicContext context, TemplateRule chosen)
        {
            TemplateRule? rival = FindRule(node, mode, ref context, int.MaxValue, chosen);

            // Another pattern of the same template is not a rival: a template matching twice is one match.
            return rival is TemplateRule second
                && !ReferenceEquals(chosen.Template, second.Template)
                && second.Template.ImportPrecedence == chosen.Template.ImportPrecedence
                && second.Priority == chosen.Priority;
        }

        /// <summary>
        /// Whether another rule of the same import precedence and priority as the one chosen also matches
        /// an item that is not a node, which a mode declared <c>on-multiple-match="fail"</c> refuses.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="mode">The mode.</param>
        /// <param name="context">The context.</param>
        /// <param name="chosen">The rule found for the item.</param>
        public bool HasRivalOfEqualRankForItem(
            XPathValue item, int mode, ref DynamicContext context, TemplateRule chosen)
        {
            TemplateRule? rival = FindForItem(item, mode, ref context, chosen);

            return rival is TemplateRule second
                && !ReferenceEquals(chosen.Template, second.Template)
                && second.Template.ImportPrecedence == chosen.Template.ImportPrecedence
                && second.Priority == chosen.Priority;
        }

        private TemplateRule? FindRule(
            int node,
            int mode,
            ref DynamicContext context,
            int maximumPrecedence,
            TemplateRule? after = null,
            int minimumPrecedence = int.MinValue)
        {
            bool skipping = after is not null;
            bool passed = false;
            NodeKind kind = context.Tree.KindOf(node);
            long key = MakeKey(mode, kind, context.Tree.FingerprintOf(node));

            List<TemplateRule>? named = m_byName.TryGetValue(key, out List<TemplateRule>? byName) ? byName : null;
            List<TemplateRule>? generic = m_byMode.TryGetValue(mode, out List<TemplateRule>? byMode) ? byMode : null;

            int namedIndex = 0;
            int genericIndex = 0;

            // Both lists are already ordered, so walk them together and test in descending priority.
            while (true)
            {
                bool hasNamed = named is not null && namedIndex < named.Count;
                bool hasGeneric = generic is not null && genericIndex < generic.Count;

                if (!hasNamed && !hasGeneric)
                {
                    return null;
                }

                bool takeNamed = hasNamed
                    && (!hasGeneric || Precedes(named![namedIndex], generic![genericIndex]));

                TemplateRule rule = takeNamed ? named![namedIndex++] : generic![genericIndex++];

                if (rule.Template.ImportPrecedence >= maximumPrecedence
                    || rule.Template.ImportPrecedence < minimumPrecedence)
                {
                    continue;
                }

                if (skipping)
                {
                    // The one already running is the last to pass over; the search proper starts after it.
                    if (!Reached(rule, after!.Value, ref passed))
                    {
                        continue;
                    }

                    skipping = false;
                }

                if (rule.Pattern.Matches(node, ref context))
                {
                    return rule;
                }
            }
        }

        private static void Add<TKey>(Dictionary<TKey, List<TemplateRule>> buckets, TKey key, TemplateRule rule)
            where TKey : notnull
        {
            if (!buckets.TryGetValue(key, out List<TemplateRule>? bucket))
            {
                bucket = new List<TemplateRule>();
                buckets.Add(key, bucket);
            }

            bucket.Add(rule);
        }

        /// <summary>
        /// Orders rules by descending priority. When priorities tie, XSLT 1.0 permits a processor to recover
        /// by taking the one declared last, which is what the secondary ordering does.
        /// </summary>
        private static void Sort(List<TemplateRule> rules)
        {
            rules.Sort(static (left, right) => Precedes(left, right) ? -1 : Precedes(right, left) ? 1 : 0);
        }

        private static bool Precedes(TemplateRule left, TemplateRule right)
        {
            // Import precedence is decided before priority is even considered.
            if (left.Template.ImportPrecedence != right.Template.ImportPrecedence)
            {
                return left.Template.ImportPrecedence > right.Template.ImportPrecedence;
            }

            if (left.Priority != right.Priority)
            {
                return left.Priority > right.Priority;
            }

            return left.Template.Index > right.Template.Index;
        }

        private static long MakeKey(int mode, NodeKind kind, int fingerprint)
        {
            return ((long)(mode + 1) << 40) | ((long)kind << 32) | (uint)fingerprint;
        }
    }
}
