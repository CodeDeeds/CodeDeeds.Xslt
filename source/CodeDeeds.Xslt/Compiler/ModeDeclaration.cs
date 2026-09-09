namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// What a mode does with a node no template rule matches.
    /// </summary>
    /// <remarks>
    /// XSLT 3.0 §6.6.1. Before 3.0 there was one built-in rule set and no way to ask for another, which is
    /// <see cref="TextOnlyCopy"/> here — containers recurse, text and attributes give up their value, and
    /// everything else produces nothing. The other five exist because that one answer is wrong for most of
    /// what stylesheets are actually written to do: a stylesheet changing three elements of a document wants
    /// <see cref="ShallowCopy"/>, and one extracting three elements from it wants <see cref="DeepSkip"/>.
    /// </remarks>
    internal enum OnNoMatch : byte
    {
        /// <summary>Recurse into children; copy the value of text and attributes. The rule before 3.0.</summary>
        TextOnlyCopy,

        /// <summary>Copy the node itself, then apply templates to its attributes and children.</summary>
        ShallowCopy,

        /// <summary>Copy the node and everything under it, matching nothing further.</summary>
        DeepCopy,

        /// <summary>Produce nothing for the node, but apply templates to its attributes and children.</summary>
        ShallowSkip,

        /// <summary>Produce nothing, and go no further down.</summary>
        DeepSkip,

        /// <summary>Refuse the transformation: <c>XTDE0555</c>.</summary>
        Fail,
    }

    /// <summary>
    /// What <c>xsl:mode</c> declares about one mode.
    /// </summary>
    /// <remarks>
    /// A mode exists whether or not it is declared — naming one on a template rule is enough to create it —
    /// so this is what a stylesheet says <em>about</em> a mode rather than the mode itself. Undeclared modes
    /// get <see cref="Default"/>, which is the behaviour every mode had before 3.0.
    /// </remarks>
    /// <param name="OnNoMatch">What to do with a node no rule in this mode matches.</param>
    /// <param name="WarnOnNoMatch">Whether to report such a node through the message writer.</param>
    /// <param name="FailOnMultipleMatch">
    /// Whether two rules of one precedence and priority both matching a node is <c>XTDE0540</c> rather than
    /// the later one winning, which is <c>on-multiple-match="fail"</c>.
    /// </param>
    internal readonly record struct ModeDeclaration(
        OnNoMatch OnNoMatch, bool WarnOnNoMatch, bool FailOnMultipleMatch = false, bool Typed = false)
    {
        /// <summary>The rules a mode follows when the stylesheet says nothing about it.</summary>
        public static ModeDeclaration Default => new ModeDeclaration(OnNoMatch.TextOnlyCopy, false);
    }
}
