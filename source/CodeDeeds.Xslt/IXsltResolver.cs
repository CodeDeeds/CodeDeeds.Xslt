namespace CodeDeeds.Xslt
{
    /// <summary>
    /// Supplies somewhere for <c>xsl:result-document</c> to write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from the resolvers that read, and for a stronger reason than those are separate from each
    /// other. <c>xsl:result-document</c> is the one instruction that lets a stylesheet <em>create</em>
    /// something outside the transformation, and a stylesheet is data as often as it is code. Without a
    /// resolver the instruction fails, so a stylesheet from an untrusted source cannot write a file however
    /// it is written.
    /// </para>
    /// <para>
    /// The caller decides what a URI means. Writing to the file system is one answer; collecting the results
    /// in memory, keyed by name, is another, and is what a caller transforming into a package usually wants.
    /// </para>
    /// </remarks>
    public interface IXsltResultResolver
    {
        /// <summary>
        /// Returns a writer for a secondary result document.
        /// </summary>
        /// <param name="href">The <c>href</c> the stylesheet asked for, after any value template.</param>
        /// <param name="baseUri">The stylesheet's base URI, if one was configured.</param>
        /// <returns>
        /// The writer to use. The engine flushes it when the document is complete but does not dispose it:
        /// the caller owns whatever it opened and knows when it is finished with.
        /// </returns>
        TextWriter Resolve(string href, string? baseUri);
    }

    /// <summary>
    /// Supplies somewhere for <c>xsl:result-document</c> to write bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same posture as <see cref="IXsltResultResolver"/> — nothing is written without one — and the same
    /// ownership: the engine builds a writer over the stream, flushes it, and leaves the stream open for
    /// whoever opened it.
    /// </para>
    /// <para>
    /// What it adds is the encoding. Each <c>xsl:result-document</c> may declare an <c>xsl:output</c> of its
    /// own, and handing back a <see cref="TextWriter"/> means its <c>encoding</c> reaches nothing but the
    /// text of the XML declaration — the result can claim <c>ISO-8859-1</c> and be UTF-8. Given a stream the
    /// engine produces the bytes, so the declaration is true, <c>byte-order-mark</c> is a mark, and a
    /// character the encoding cannot hold becomes a character reference rather than a question mark.
    /// </para>
    /// <para>
    /// Set on <see cref="XsltOptions.ResultStreamResolver"/>, and takes precedence over
    /// <see cref="XsltOptions.ResultResolver"/> where both are supplied.
    /// </para>
    /// </remarks>
    public interface IXsltResultStreamResolver
    {
        /// <summary>
        /// Returns a stream for a secondary result document.
        /// </summary>
        /// <param name="href">The <c>href</c> the stylesheet asked for, after any value template.</param>
        /// <param name="baseUri">The stylesheet's base URI, if one was configured.</param>
        /// <returns>
        /// The stream to write to. The engine writes and flushes but does not dispose it: the caller owns
        /// whatever it opened and knows when it is finished with.
        /// </returns>
        Stream Resolve(string href, string? baseUri);
    }

    /// <summary>
    /// A stylesheet located by <see cref="IXsltResolver"/>, together with the identity that nested
    /// references inside it resolve against.
    /// </summary>
    public sealed class ResolvedResource
    {
        /// <summary>Initializes a resolved stylesheet.</summary>
        /// <param name="reader">The stylesheet text. The compiler disposes it.</param>
        /// <param name="uri">
        /// An absolute identity for the stylesheet. Used as the base for references inside it, and to detect a
        /// reference cycle, so it must be the same string every time the same stylesheet is returned.
        /// </param>
        public ResolvedResource(TextReader reader, string uri)
        {
            Reader = reader;
            Uri = uri;
        }

        /// <summary>Gets the stylesheet text.</summary>
        public TextReader Reader { get; }

        /// <summary>Gets the stylesheet's absolute identity.</summary>
        public string Uri { get; }
    }

    /// <summary>
    /// Locates a resource a stylesheet refers to by href — a module named by <c>xsl:include</c> or
    /// <c>xsl:import</c>, or a document named by <c>document()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Following an <c>href</c> means letting a stylesheet decide what this process reads, so it is not
    /// something the engine does on its own. No resolver is configured by default, and without one an
    /// <c>href</c> cannot be followed at all — the same posture as a document type declaration, whose external
    /// entities are fetched only through a resolver of their own. A caller that wants modular stylesheets
    /// supplies a resolver and thereby chooses what it is willing to expose.
    /// </para>
    /// <para>
    /// <see cref="FileResolver"/> covers the usual case of stylesheets in a directory, and
    /// <see cref="UriResolver"/> the case of ones reached by URL, from the web as well as from a directory. An
    /// implementation that serves them from embedded resources, a database, or a fixed dictionary is often a
    /// better fit, and gives up nothing.
    /// </para>
    /// </remarks>
    public interface IXsltResolver
    {
        /// <summary>
        /// Locates a referenced stylesheet.
        /// </summary>
        /// <param name="href">The reference exactly as written in the stylesheet.</param>
        /// <param name="baseUri">
        /// The <see cref="ResolvedResource.Uri"/> of the stylesheet containing the reference, or
        /// <see langword="null"/> for a reference in the stylesheet the caller supplied directly.
        /// </param>
        /// <returns>The stylesheet, or <see langword="null"/> if this resolver will not supply it.</returns>
        ResolvedResource? Resolve(string href, string? baseUri);
    }

    /// <summary>
    /// A package resolver that knows which versions of a package it holds, so that an
    /// <c>xsl:use-package</c> naming a range of versions can be given the one it takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine asks <see cref="VersionsOf"/> for what is on offer under a name, chooses by the
    /// specification's rules — the highest version the range takes — and then asks
    /// <see cref="IXsltResolver.Resolve"/> for it, passing the chosen version where a stylesheet's base
    /// URI would otherwise go. A resolver that holds one version of everything, or does not know, may
    /// return an empty list and will be asked with <see langword="null"/> as before; what the package it
    /// supplies declares of its own version is then checked against the range.
    /// </para>
    /// <para>
    /// The versions are strings as the packages declare them, in the grammar of XSLT 3.0 §3.5.1; one the
    /// engine cannot read is passed over.
    /// </para>
    /// </remarks>
    public interface IXsltPackageResolver : IXsltResolver
    {
        /// <summary>
        /// Returns the versions available of the package with a name, as those packages declare them.
        /// </summary>
        /// <param name="name">The package name, exactly as the <c>xsl:use-package</c> wrote it.</param>
        /// <returns>The versions, or an empty list where none are known.</returns>
        IReadOnlyList<string> VersionsOf(string name);
    }
}
