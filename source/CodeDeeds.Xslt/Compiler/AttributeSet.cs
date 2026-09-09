using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// One <c>xsl:attribute-set</c> declaration.
    /// </summary>
    /// <remarks>
    /// A name may be declared more than once — in one module or across an import — and the declarations are
    /// merged rather than one replacing the other, so each is kept separately with the precedence it came from.
    /// </remarks>
    internal sealed class AttributeSetDeclaration
    {
        /// <summary>Initializes a declaration.</summary>
        /// <param name="precedence">The import precedence of the module it was written in.</param>
        /// <param name="used">Sets this one draws in before its own attributes.</param>
        public AttributeSetDeclaration(int precedence, ExpandedName[] used)
        {
            Precedence = precedence;
            Used = used;
        }

        /// <summary>The import precedence of the declaring module.</summary>
        public int Precedence { get; }

        /// <summary>The sets named by this declaration's own <c>use-attribute-sets</c>.</summary>
        public ExpandedName[] Used { get; set; }

        /// <summary>The <c>xsl:attribute</c> instructions the declaration contains.</summary>
        public Instruction[] Attributes { get; set; } = Array.Empty<Instruction>();

        /// <summary>
        /// Whether the declaration names the set without defining it, leaving that to a using package.
        /// </summary>
        public bool IsAbstract { get; set; }

        /// <summary>
        /// How many slots the declaration's own local variables need.
        /// </summary>
        /// <remarks>
        /// An attribute set's body is a sequence constructor, so it may declare local variables, and those
        /// need a frame of their own. Running them in the caller's frame is worse than a crash where the
        /// caller's frame happens to be large enough: the set would then quietly overwrite one of the
        /// caller's variables at whatever slot it had been given.
        /// </remarks>
        public int FrameSize { get; set; }
    }

    /// <summary>
    /// Every declaration of one attribute set name, ordered so that applying them in sequence gives the right
    /// result.
    /// </summary>
    /// <remarks>
    /// Attributes are written in order and a later write of the same name wins, so ordering the declarations by
    /// ascending precedence makes the highest-precedence definition of each attribute the one that survives —
    /// no per-attribute comparison needed.
    /// </remarks>
    internal sealed class AttributeSet
    {
        private readonly List<AttributeSetDeclaration> m_declarations = new();

        /// <summary>Initializes an attribute set.</summary>
        /// <param name="name">The set's expanded name.</param>
        public AttributeSet(ExpandedName name)
        {
            Name = name;
        }

        /// <summary>The set's expanded name.</summary>
        public ExpandedName Name { get; }

        /// <summary>The declarations contributing to this set, in ascending precedence.</summary>
        public IReadOnlyList<AttributeSetDeclaration> Declarations => m_declarations;

        /// <summary>
        /// Whether the set is named but nowhere defined, every declaration of it being abstract.
        /// </summary>
        /// <remarks>
        /// Asked of the set rather than of a declaration because that is where the answer lives. A package
        /// declaring the set abstract and another supplying it produce two declarations of one name, exactly
        /// as an import does, and the supplied one being present is what makes the set defined.
        /// </remarks>
        public bool IsAbstract
        {
            get
            {
                foreach (AttributeSetDeclaration declaration in m_declarations)
                {
                    if (!declaration.IsAbstract)
                    {
                        return false;
                    }
                }

                return m_declarations.Count > 0;
            }
        }

        /// <summary>Adds a declaration, keeping the list ordered by precedence.</summary>
        /// <param name="declaration">The declaration to add.</param>
        public void Add(AttributeSetDeclaration declaration)
        {
            int index = m_declarations.Count;
            while (index > 0 && m_declarations[index - 1].Precedence > declaration.Precedence)
            {
                index--;
            }

            m_declarations.Insert(index, declaration);
        }
    }

    /// <summary>
    /// Writes the attributes of one or more attribute sets onto the element being built.
    /// </summary>
    /// <remarks>
    /// Shared by literal result elements, <c>xsl:element</c> and <c>xsl:copy</c>, and used again for the
    /// <c>use-attribute-sets</c> a set may itself carry.
    /// </remarks>
    internal static class AttributeSetApplier
    {
        /// <summary>
        /// Applies the named sets, in the order written.
        /// </summary>
        /// <param name="names">The sets to apply.</param>
        /// <param name="context">The context the attribute values are evaluated in.</param>
        /// <param name="runtime">The transformation in progress.</param>
        public static void Apply(
            ExpandedName[] names,
            ref DynamicContext context,
            XsltRuntime runtime)
        {
            foreach (ExpandedName name in names)
            {
                AttributeSet? set = runtime.FindAttributeSet(name);
                if (set is null)
                {
                    continue;
                }

                // An abstract declaration has no attributes to write, so it contributes nothing and is
                // skipped rather than run. It is an error only where nothing else defines the set: the point
                // of declaring one abstract is that a using package supplies it, and where one has, that
                // declaration is here at a higher precedence and is what applying the set means.
                if (set.IsAbstract)
                {
                    throw XsltRuntime.AbstractComponent("attribute set", name.LocalName);
                }

                foreach (AttributeSetDeclaration declaration in set.Declarations)
                {
                    if (declaration.IsAbstract)
                    {
                        continue;
                    }

                    // A set's own use-attribute-sets are applied first, so its attributes can override them.
                    Apply(declaration.Used, ref context, runtime);

                    // Its own frame, so a local variable in the set lives somewhere of its own rather than
                    // in whatever slot the calling template had at that index. Everything else about the
                    // focus is the caller's, since an attribute value is about the node being processed.
                    DynamicContext inner = context;
                    inner.Locals = declaration.FrameSize == 0
                        ? Array.Empty<XPathValue>()
                        : new XPathValue[declaration.FrameSize];

                    inner.FrameBase = 0;

                    Instruction.ExecuteAll(declaration.Attributes, ref inner, runtime);
                }
            }
        }
    }
}
