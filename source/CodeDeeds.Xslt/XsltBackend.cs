namespace CodeDeeds.Xslt
{
    /// <summary>
    /// How a compiled stylesheet executes the expressions it contains.
    /// </summary>
    /// <remarks>
    /// Both backends run the same intermediate representation and must produce identical results; the
    /// interpreter doubles as the reference implementation the emitted code is verified against.
    /// </remarks>
    public enum XsltBackend
    {
        /// <summary>
        /// Walk the expression tree directly. Always available, and the behaviour every other backend is
        /// checked against.
        /// </summary>
        Interpreted = 0,

        /// <summary>
        /// Emit IL for each expression with <see cref="System.Reflection.Emit"/>, removing the per-node
        /// dispatch from evaluation. Worth choosing for a stylesheet that will be used many times; see the
        /// remarks for what it costs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The gain depends on how much of the transformation is expression evaluation rather than reading the
        /// input. It is slight on a transform that mostly copies a document through — a few per cent, because
        /// building the input tree dominates and no backend affects that — and reaches about twice the
        /// interpreter's speed on predicate-bound work over a document of any size.
        /// </para>
        /// <para>
        /// What it costs is paid before any of that. Emitting roughly doubles the time to construct an
        /// <see cref="Xslt"/>, and the first transform in a process pays a further fixed cost of some
        /// milliseconds while <see cref="System.Reflection.Emit"/> is loaded and the emitted methods are
        /// themselves compiled. That fixed part falls once per process rather than once per stylesheet.
        /// </para>
        /// <para>
        /// Measured on this engine, the two meet at roughly thirty transformations of the same stylesheet.
        /// Below that <see cref="Interpreted"/> finishes the whole job sooner, which is why it is the default.
        /// </para>
        /// </remarks>
        Compiled = 1,
    }
}
