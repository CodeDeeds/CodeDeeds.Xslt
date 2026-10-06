using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// What an evaluation reads and does not change while it runs: the transformation it belongs to and
    /// that transformation's global variables, what answers <c>doc()</c> and <c>unparsed-text()</c> where
    /// no transformation does, the collations, the name slots and the clock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the part of a <see cref="DynamicContext"/> that is the same in every context of an
    /// evaluation, and they were fields of it. A context is a struct copied at every change of focus — at
    /// each node a template is applied to, each item a predicate tests, each call of a function — and what
    /// a copy costs goes by the size: with these seven in it the context was 144 bytes holding twelve
    /// references, and a copy of one returned from a method was a call that moved the bytes and then marked
    /// every reference for the garbage collector, some twenty nanoseconds. Behind one reference it is 96
    /// bytes, which the just-in-time compiler copies in place, in six.
    /// </para>
    /// <para>
    /// Nothing in one changes once it is made. A transformation makes one and every context in it refers
    /// to the same one; a context given something else — by a setter of <see cref="DynamicContext"/>, as
    /// <c>context.Globals = values</c> — gets a copy of its own with the one thing changed, which is for the
    /// setting up of a context and not for a loop.
    /// </para>
    /// </remarks>
    public sealed class Surroundings
    {
        /// <summary>
        /// Surroundings with nothing in them: no transformation, no globals, nothing to answer <c>doc()</c>.
        /// What a context has that was given none.
        /// </summary>
        public static readonly Surroundings None = new();

        /// <summary>Makes empty surroundings.</summary>
        public Surroundings()
        {
        }

        private Surroundings(Surroundings from)
        {
            Runtime = from.Runtime;
            Globals = from.Globals;
            DocumentLoader = from.DocumentLoader;
            TextLoader = from.TextLoader;
            Collations = from.Collations;
            Names = from.Names;
            Clock = from.Clock;
        }

        /// <summary>
        /// The transformation in progress, or <see langword="null"/> when an expression is evaluated outside
        /// one. Expressions need it to resolve names against a tree other than the one the context is on,
        /// which happens when a path navigates into a result tree fragment.
        /// </summary>
        public XsltRuntime? Runtime { get; init; }

        /// <summary>Backing store for global variables and parameters.</summary>
        public XPathValue[] Globals { get; init; } = Array.Empty<XPathValue>();

        /// <summary>
        /// What answers <c>doc()</c> where no transformation is running — a static expression at compile
        /// time — or <see langword="null"/> where nothing does. Given the reference as written and the base
        /// URI to resolve it against, or null for the module's own.
        /// </summary>
        public Func<string, string?, XdmTree>? DocumentLoader { get; init; }

        /// <summary>
        /// What answers <c>unparsed-text()</c> where no transformation is running, or
        /// <see langword="null"/> where nothing does. Given the reference as written and the encoding
        /// the call named, or null for none.
        /// </summary>
        /// <remarks>
        /// The companion of <see cref="DocumentLoader"/>, and there for the same reason: an
        /// expression evaluated on its own has no transformation behind it and so no resolver of its
        /// own, and a caller that means it to read something lends it one.
        /// </remarks>
        public Func<string, string?, string>? TextLoader { get; init; }

        /// <summary>
        /// The caller's collations where no transformation is running to carry them — a static expression
        /// at compile time, or an expression evaluated on its own — or <see langword="null"/> where there
        /// are none. A running transformation answers from its own options instead.
        /// </summary>
        public IXsltCollationResolver? Collations { get; init; }

        /// <summary>
        /// The slots the expression's name tests were assigned, or <see langword="null"/> where they were not
        /// supplied. Outside a transformation this is what lets a path into a document built while the
        /// expression ran — by <c>fn:parse-xml</c> or <c>fn:json-to-xml</c> — resolve its names at all.
        /// </summary>
        public NameSlotTable? Names { get; init; }

        /// <summary>
        /// The one reading of the clock this evaluation makes, shared by every <c>current-*</c> call in it,
        /// or <see langword="null"/> where none has been made yet.
        /// </summary>
        /// <remarks>
        /// A transformation's clock is the runtime's and this stays null; an expression evaluated outside
        /// one is given a clock the first time it asks, by <see cref="DynamicContext.ReadClock"/>, which is
        /// what makes it stable within itself.
        /// </remarks>
        public Clock? Clock { get; init; }

        /// <summary>Returns a copy of these surroundings with another transformation.</summary>
        internal Surroundings WithRuntime(XsltRuntime? runtime) => new(this) { Runtime = runtime };

        /// <summary>Returns a copy of these surroundings with another store of globals.</summary>
        internal Surroundings WithGlobals(XPathValue[] globals) => new(this) { Globals = globals };

        /// <summary>Returns a copy of these surroundings with another answer to <c>doc()</c>.</summary>
        internal Surroundings WithDocumentLoader(Func<string, string?, XdmTree>? loader) => new(this) { DocumentLoader = loader };

        /// <summary>Returns a copy of these surroundings with another answer to <c>unparsed-text()</c>.</summary>
        internal Surroundings WithTextLoader(Func<string, string?, string>? loader) => new(this) { TextLoader = loader };

        /// <summary>Returns a copy of these surroundings with other collations.</summary>
        internal Surroundings WithCollations(IXsltCollationResolver? collations) => new(this) { Collations = collations };

        /// <summary>Returns a copy of these surroundings with another table of name slots.</summary>
        internal Surroundings WithNames(NameSlotTable? names) => new(this) { Names = names };

        /// <summary>Returns a copy of these surroundings with another clock.</summary>
        internal Surroundings WithClock(Clock? clock) => new(this) { Clock = clock };
    }
}
