namespace CodeDeeds.Xslt
{
    /// <summary>
    /// Reports an error in a stylesheet, an XPath expression, or the execution of a transformation.
    /// </summary>
    /// <remarks>
    /// Errors that the specifications name carry the name in <see cref="Code"/>, alongside the message. The
    /// two serve different readers: the message is for whoever has to fix the stylesheet, and the code is for
    /// anything that has to react to the error in particular — a conformance suite checking that the right
    /// error was raised, or a caller catching one kind and letting the rest through.
    /// </remarks>
    public class XsltException : Exception
    {
        /// <summary>Initializes a new instance with a message.</summary>
        /// <param name="message">A description of the error.</param>
        public XsltException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance with a message and an underlying cause.</summary>
        /// <param name="message">A description of the error.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public XsltException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>Initializes a new instance with a specification error code and a message.</summary>
        /// <param name="code">The error code the specification gives this error; see <see cref="XsltErrors"/>.</param>
        /// <param name="message">A description of the error.</param>
        public XsltException(XsltErrorCode code, string message)
            : base(message)
        {
            Code = code.ToString();
        }

        /// <summary>
        /// Initializes a new instance carrying a code the stylesheet named rather than one this engine chose.
        /// </summary>
        /// <remarks>
        /// Deliberately separate from the <see cref="XsltErrorCode"/> constructor, and deliberately not
        /// public. The enumeration exists so that a throw site cannot invent a code by mistyping one, and
        /// there is exactly one place where the code is not the engine's to choose: <c>fn:error()</c> relays
        /// whatever name the stylesheet raised, which may be one of its own and so cannot be enumerated here.
        /// </remarks>
        /// <param name="message">A description of the error.</param>
        /// <param name="code">The code, which is the local part of the name the stylesheet raised.</param>
        internal XsltException(string message, string code)
            : base(message)
        {
            Code = code;
        }

        /// <summary>
        /// Gets the error code the specification gives this error, or <see langword="null"/> where the error
        /// is one this engine reports on its own account and no specification names.
        /// </summary>
        public string? Code { get; init; }

        /// <summary>
        /// Gets the namespace of the error code, or <see langword="null"/> where the code is one of the
        /// specifications' own, which all live in the <c>err</c> namespace.
        /// </summary>
        /// <remarks>
        /// Only a stylesheet names an error outside that namespace, through <c>fn:error()</c>; an
        /// <c>xsl:catch</c> matches the code by its whole name, so the namespace has to travel with it.
        /// </remarks>
        public string? CodeNamespace { get; init; }

        /// <summary>
        /// Gets what the stylesheet attached to the error, which an <c>xsl:catch</c> reads as
        /// <c>$err:value</c>, or <see langword="null"/> where nothing was attached.
        /// </summary>
        /// <remarks>
        /// Two things put anything here, and both are a stylesheet handing whoever catches the error
        /// something to look at rather than only a sentence to read: the third argument of
        /// <c>fn:error()</c>, and the content of a terminating <c>xsl:message</c>. Internal because it is an
        /// XDM sequence, which is the engine's currency rather than a caller's.
        /// </remarks>
        internal XPath.XPathValue? Value { get; init; }

        /// <summary>
        /// Gets the URI of the stylesheet module the failing instruction stands in, or <see langword="null"/>
        /// where the error was not raised by an instruction, or where the stylesheet was read without one.
        /// </summary>
        public string? Module { get; internal set; }

        /// <summary>Gets the line the failing instruction starts on in its module, or 0 where unknown.</summary>
        public int Line { get; internal set; }

        /// <summary>Gets the column the failing instruction starts at on its line, or 0 where unknown.</summary>
        public int Column { get; internal set; }
    }

    /// <summary>
    /// The error codes this engine raises, as named by XSLT, XPath, XQuery and their Functions and Operators.
    /// </summary>
    /// <remarks>
    /// An enumeration rather than loose strings, so that a throw site cannot invent a code by mistyping one
    /// and so that the set is discoverable. The names are the codes: a code appears in a message and in a
    /// test expectation exactly as it is spelled here. The prefix says which specification names it —
    /// <c>XTSE</c> a static error in a stylesheet, <c>XP</c> one in an XPath expression, <c>FO</c> one raised
    /// by the function library.
    /// </remarks>
    public enum XsltErrorCode
    {
        /// <summary>
        /// An XSLT element in a place it is not allowed, without an attribute it requires, or holding
        /// content it may not hold.
        /// </summary>
        XTSE0010,

        /// <summary>An attribute whose value must be one of a fixed set, and is not.</summary>
        XTSE0020,

        /// <summary>An XSLT element carrying an attribute the specification does not define for it.</summary>
        XTSE0090,

        /// <summary>A schema import, where the processor is not schema-aware.</summary>
        XTSE1650,

        /// <summary>Validation or a type annotation was asked for, where the processor is not schema-aware.</summary>
        XTSE1660,

        /// <summary>A <c>match</c> attribute whose value does not fit the pattern grammar.</summary>
        XTSE0340,

        /// <summary>An <c>xsl:variable</c> or <c>xsl:param</c> with both a <c>select</c> and content.</summary>
        XTSE0620,

        /// <summary>An <c>xsl:attribute</c> with both a <c>select</c> and content.</summary>
        XTSE0840,

        /// <summary>An <c>xsl:value-of</c> with both a <c>select</c> and content.</summary>
        XTSE0870,

        /// <summary>An <c>xsl:processing-instruction</c> with both a <c>select</c> and content.</summary>
        XTSE0880,

        /// <summary>An <c>xsl:namespace</c> with both a <c>select</c> and content.</summary>
        XTSE0910,

        /// <summary>An <c>xsl:comment</c> with both a <c>select</c> and content.</summary>
        XTSE0940,

        /// <summary>The caller named a template to start at, and the stylesheet declares no such template.</summary>
        XTDE0040,

        /// <summary>
        /// The caller named a function to start at, and the stylesheet declares no public function of that
        /// name and arity.
        /// </summary>
        XTDE0041,

        /// <summary>The caller named a mode to start in, and no template declares that mode.</summary>
        XTDE0045,


        /// <summary>An <c>xsl:apply-imports</c> or <c>xsl:next-match</c> with no current template rule.</summary>
        XTDE0560,
        /// <summary>A key defined in terms of itself, directly or through another key.</summary>
        XTDE0640,

        /// <summary>A computed <c>lang</c> whose value is not a language code.</summary>
        XTDE0030,


        /// <summary>Two <c>xsl:output</c> of one definition and precedence giving one attribute two values.</summary>
        XTSE1560,
        /// <summary>An <c>xsl:output</c> whose <c>method</c> is not a name an output method may have.</summary>
        XTSE1570,

        /// <summary>Text where a stylesheet holds declarations.</summary>
        XTSE0120,

        /// <summary>An <c>xsl:include</c> somewhere other than at the top level.</summary>
        XTSE0170,

        /// <summary>An <c>xsl:import</c> somewhere other than at the top level.</summary>
        XTSE0190,

        /// <summary>
        /// An <c>xsl:import</c> after another top-level element, which XSLT 2.0 forbids and 3.0 allows.
        /// </summary>
        XTSE0200,

        /// <summary>An XSLT element that must be empty, and is not.</summary>
        XTSE0260,

        /// <summary>A <c>mode</c> on <c>xsl:template</c> that is not a list of distinct mode names.</summary>
        XTSE0550,

        /// <summary>Two global variables or parameters of one name at one import precedence.</summary>
        XTSE0630,

        /// <summary>Two <c>xsl:with-param</c> of one name on one call.</summary>
        XTSE0670,

        /// <summary>A <c>use-attribute-sets</c> naming an attribute set the stylesheet does not declare.</summary>
        XTSE0710,

        /// <summary>An <c>xsl:call-template</c> naming a template the containing package cannot see.</summary>
        XTSE0650,

        /// <summary>A package left able to see two components of one name, and hiding neither.</summary>
        XTSE3050,

        /// <summary>A declaration in an <c>xsl:override</c> homonymous with another in the using package.</summary>
        XTSE3055,

        /// <summary>A declaration in an <c>xsl:override</c> matching no component of the used package.</summary>
        XTSE3058,

        /// <summary>An override whose signature is not compatible with the component it overrides.</summary>
        XTSE3070,

        /// <summary>An <c>xsl:use-package</c> in a module reached by <c>xsl:import</c>.</summary>
        XTSE3008,

        /// <summary>An <c>xsl:import</c> or <c>xsl:include</c> of something that is not a stylesheet module.</summary>
        XTSE0165,

        /// <summary>An <c>xsl:override</c> of a component that is neither public nor abstract.</summary>
        XTSE3060,

        /// <summary>A package meant to be run referring to a component that is abstract.</summary>
        XTSE3080,

        /// <summary>An <c>xsl:sequence</c> with both a <c>select</c> attribute and content.</summary>
        XTSE3185,

        /// <summary>One NameTest in both an <c>xsl:strip-space</c> and an <c>xsl:preserve-space</c> at one precedence.</summary>
        XTSE0270,

        /// <summary>Two <c>xsl:key</c> declarations of one name disagreeing about <c>composite</c>.</summary>
        XTSE1222,

        /// <summary>An <c>xsl:break</c> or <c>xsl:next-iteration</c> that is not in a tail position.</summary>
        XTSE3120,

        /// <summary>An <c>xsl:break</c> or <c>xsl:on-completion</c> with both a <c>select</c> and content.</summary>
        XTSE3125,

        /// <summary>An <c>xsl:try</c> with a <c>select</c> and children other than <c>xsl:catch</c>.</summary>
        XTSE3140,

        /// <summary>An <c>xsl:catch</c> with both a <c>select</c> and content.</summary>
        XTSE3150,

        /// <summary>Two sibling <c>xsl:merge-source</c> elements of one name.</summary>
        XTSE3190,

        /// <summary>An <c>xsl:map-entry</c> with both a <c>select</c> and content.</summary>
        XTSE3280,

        /// <summary>An <c>xsl:apply-imports</c> in a template rule declared inside an <c>xsl:override</c>.</summary>
        XTSE3460,

        /// <summary>A parameter of <c>xsl:iterate</c> that is implicitly mandatory.</summary>
        XTSE3520,

        /// <summary>A target expression of <c>xsl:evaluate</c> that cannot be evaluated: not an expression, or calling what it may not.</summary>
        XTDE3160,

        /// <summary>An element or attribute processed in a mode declared <c>typed</c>, which takes no untyped node.</summary>
        XTTE3100,

        /// <summary>A <c>with-params</c> of <c>xsl:evaluate</c> that is not one map from QNames.</summary>
        XTTE3165,

        /// <summary>A <c>namespace-context</c> of <c>xsl:evaluate</c> that is not one node.</summary>
        XTTE3170,

        /// <summary>An <c>xsl:evaluate</c> reached with dynamic evaluation switched off.</summary>
        XTDE3175,

        /// <summary>A <c>context-item</c> of <c>xsl:evaluate</c> selecting more than one item.</summary>
        XTTE3210,

        /// <summary>An unparsed-entity function asked about a node in a tree not rooted at a document node.</summary>
        XTDE1370,

        /// <summary><c>unparsed-entity-public-id()</c> called with no context node, or in a tree whose root is not a document node.</summary>
        XTDE1380,

        /// <summary>A character map named that no <c>xsl:character-map</c> in the package declares.</summary>
        XTSE1590,

        /// <summary>An initial mode named with no source document to apply templates to.</summary>
        XTDE0044,

        /// <summary>A map or a function item where a node was being built.</summary>
        XTDE0450,

        /// <summary>More than one template rule matching where the mode says that is a failure.</summary>
        XTDE0540,

        /// <summary><c>current-group()</c> called where there is no current group.</summary>
        XTDE1061,

        /// <summary><c>current-grouping-key()</c> called where there is no current grouping key.</summary>
        XTDE1071,

        /// <summary>A relative reference to <c>document()</c> with no base URI to resolve it against.</summary>
        XTDE1162,

        /// <summary>The argument of <c>type-available()</c> is not a name.</summary>
        XTDE1428,

        /// <summary>The <c>select</c> of <c>xsl:copy</c> yielding more than one item.</summary>
        XTTE3180,

        /// <summary>An accumulator function called with no node, or an attribute or namespace node, as the context item.</summary>
        XTTE3360,

        /// <summary>An <c>exclude-result-prefixes</c> naming a prefix nothing in scope binds.</summary>
        XTSE0808,

        /// <summary>An <c>exclude-result-prefixes</c> saying <c>#default</c> where there is none.</summary>
        XTSE0809,

        /// <summary>An <c>extension-element-prefixes</c> naming a prefix nothing in scope binds.</summary>
        XTSE1430,

        /// <summary>An <c>xsl:number</c> with both a <c>value</c> and an attribute that counts nodes.</summary>
        XTSE0975,

        /// <summary>An <c>xsl:template</c> with neither a <c>match</c> nor a <c>name</c>, or with a
        /// <c>mode</c> or <c>priority</c> and no <c>match</c>.</summary>
        XTSE0500,

        /// <summary>Two sibling <c>xsl:param</c> of one name.</summary>
        XTSE0580,

        /// <summary>An <c>xsl:param</c> inside <c>xsl:function</c> given a default value.</summary>
        XTSE0760,

        /// <summary>Two <c>xsl:function</c> of one name and arity at one import precedence.</summary>
        XTSE0770,

        /// <summary>Two <c>xsl:mode</c> declarations of one mode at one import precedence that disagree.</summary>
        XTSE0545,

        /// <summary>A node reached in a mode declared <c>on-no-match="fail"</c> that no rule matched.</summary>
        XTDE0555,

        /// <summary>An <c>xsl:context-item</c> saying the item is absent and declaring a type for it.</summary>
        XTSE3088,

        /// <summary>An <c>xsl:global-context-item</c> declared twice, or two of them disagreeing.</summary>
        XTSE3087,

        /// <summary>An <c>xsl:global-context-item</c> saying the item is absent and declaring its type.</summary>
        XTSE3089,

        /// <summary>An <c>xsl:try</c> with both a <c>select</c> and content.</summary>
        XTSE3130,

        /// <summary>An <c>xsl:merge-source</c> asking to stream a document this engine has to build.</summary>
        XTSE3430,

        /// <summary>A static variable redeclared at a higher precedence, after and unlike an earlier one.</summary>
        XTSE3450,

        /// <summary>Two <c>xsl:merge-source</c> with different numbers of <c>xsl:merge-key</c>.</summary>
        XTSE2200,

        /// <summary>
        /// Corresponding <c>xsl:merge-key</c> of two <c>xsl:merge-source</c> disagreeing about the ordering.
        /// </summary>
        XTDE2210,

        /// <summary>An <c>xsl:merge-source</c> whose items are not in the order its own keys put them.</summary>
        XTDE2220,

        /// <summary>Merge keys of two <c>xsl:merge-source</c> that cannot be compared with one another.</summary>
        XTTE2230,

        /// <summary>An <c>xsl:merge-source</c> with both <c>for-each-item</c> and <c>for-each-source</c>.</summary>
        XTSE3195,

        /// <summary>An <c>xsl:merge-key</c> with both a <c>select</c> and content.</summary>
        XTSE3200,

        /// <summary><c>current-merge-group()</c> written in a pattern.</summary>
        XTSE3470,

        /// <summary>
        /// <c>current-merge-group()</c> called where no <c>xsl:merge-action</c> is being evaluated.
        /// </summary>
        XTDE3480,

        /// <summary><c>current-merge-group()</c> naming a source the <c>xsl:merge</c> does not have.</summary>
        XTDE3490,

        /// <summary><c>current-merge-key()</c> written in a pattern.</summary>
        XTSE3500,

        /// <summary>
        /// <c>current-merge-key()</c> called where no <c>xsl:merge-action</c> is being evaluated.
        /// </summary>
        XTDE3510,

        /// <summary>
        /// An <c>xsl:try</c> with <c>rollback-output="no"</c> catching an error after output was written.
        /// </summary>
        XTDE3530,

        /// <summary>
        /// A template rule in an <c>xsl:override</c> whose mode is not one another package can have exposed.
        /// </summary>
        XTSE3440,

        /// <summary>
        /// A mode used in a package that declares its modes, and which no <c>xsl:mode</c> declares.
        /// </summary>
        XTSE3085,

        /// <summary>
        /// Reaching a component declared <c>visibility="abstract"</c>, which no using package supplied.
        /// </summary>
        XTDE3052,

        /// <summary>An <c>xsl:use-package</c> naming a package that cannot be found.</summary>
        XTSE3000,

        /// <summary>
        /// An <c>xsl:expose</c> giving a component a visibility its own declaration already stated
        /// otherwise.
        /// </summary>
        XTSE3010,

        /// <summary>An <c>xsl:expose</c> or <c>xsl:accept</c> naming a component that does not exist.</summary>
        XTSE3020,

        /// <summary>An <c>xsl:expose</c> saying <c>component="*"</c> and naming a component exactly.</summary>
        XTSE3022,

        /// <summary>
        /// An <c>xsl:expose</c> making a component abstract whose declaration does not declare it so.
        /// </summary>
        XTSE3025,

        /// <summary>
        /// An <c>xsl:accept</c> naming a component the package it is used from does not have.
        /// </summary>
        XTSE3030,

        /// <summary>An <c>xsl:accept</c> saying <c>component="*"</c> and naming a component exactly.</summary>
        XTSE3032,

        /// <summary>
        /// An <c>xsl:accept</c> taking a component with more visibility than the package offering it gave.
        /// </summary>
        XTSE3040,

        /// <summary>Two <c>xsl:accumulator</c> declarations of one name.</summary>
        XTSE3350,

        /// <summary>An accumulator function naming an accumulator the stylesheet does not declare.</summary>
        XTDE3340,

        /// <summary>An accumulator asked for on a document it does not apply to.</summary>
        XTDE3362,

        /// <summary>An accumulator defined in terms of itself.</summary>
        XTDE3400,

        /// <summary>An <c>xsl:assert</c> whose test was false.</summary>
        XTMM9000,

        /// <summary>Two entries of one <c>xsl:map</c> sharing a key.</summary>
        XTDE3365,

        /// <summary>An <c>xsl:map</c> whose content is not maps, or a key that is not one atomic value.</summary>
        XTTE3375,

        /// <summary>A context item that does not match the type the template declared for it.</summary>
        XTTE3090,

        /// <summary>An <c>xsl:global-context-item</c> requiring one, where the caller supplied no document.</summary>
        XTDE3086,

        /// <summary>The source document does not match the type <c>xsl:global-context-item</c> declared.</summary>
        XTTE3086,

        /// <summary>An <c>xsl:sort</c> with both a <c>select</c> and content.</summary>
        XTSE1015,

        /// <summary>A <c>stable</c> on an <c>xsl:sort</c> that is not the first of its siblings.</summary>
        XTSE1017,

        /// <summary>An <c>xsl:perform-sort</c> with both a <c>select</c> and content of its own.</summary>
        XTSE1040,

        /// <summary>An <c>xsl:analyze-string</c> with neither substring branch.</summary>
        XTSE1130,

        /// <summary>An <c>xsl:key</c> with both a <c>use</c> and content, or with neither.</summary>
        XTSE1205,

        /// <summary>An <c>xsl:for-each-group</c> with a <c>collation</c> and no grouping key to compare.</summary>
        XTSE1090,

        /// <summary>Two <c>xsl:decimal-format</c> of one name and precedence giving one property two values.</summary>
        XTSE1290,

        /// <summary>A name declared in a namespace the specifications reserve.</summary>
        XTSE0080,

        /// <summary>
        /// An <c>xsl:accept</c> naming a component the same <c>xsl:use-package</c> overrides.
        /// </summary>
        XTSE3051,

        /// <summary>An invocation naming both an initial template and an initial mode, which 2.0 refused.</summary>
        XTDE0047,

        /// <summary>A required parameter of the template the transformation starts at, unsupplied — 2.0's name for it.</summary>
        XTDE0060,

        /// <summary>A document read and written by one transformation, under one URI.</summary>
        XTDE1500,


        /// <summary>A <c>version</c> that is not the decimal number the attribute is declared to be.</summary>
        XTSE0110,

        /// <summary>A <c>default-collation</c> naming no collation this engine has.</summary>
        XTSE0125,

        /// <summary>An element among a stylesheet's declarations that is in no namespace.</summary>
        XTSE0130,

        /// <summary>An attribute in the XSLT namespace on a literal result element that XSLT does not define.</summary>
        XTSE0805,

        /// <summary>A reserved namespace designated as an extension namespace, or named as one.</summary>
        XTSE0085,

        /// <summary>Two <c>xsl:namespace-alias</c> of one precedence sending one namespace two ways.</summary>
        XTSE0810,

        /// <summary>A parameter supplied to a template that declares no parameter of that name.</summary>
        XTSE0680,

        /// <summary><c>current-group()</c> written in a pattern.</summary>
        XTSE1060,

        /// <summary><c>current-grouping-key()</c> written in a pattern.</summary>
        XTSE1070,

        /// <summary>A two-argument <c>key()</c> where there is no context node to say which tree to search.</summary>
        XTDE1270,

        /// <summary><c>current()</c> where there is no node being processed.</summary>
        XTDE1360,

        /// <summary>An <c>xsl:result-document</c> written while a temporary tree is being built.</summary>
        XTDE1480,

        /// <summary>A <c>document()</c> reference carrying a fragment identifier this engine cannot read.</summary>
        XTRE1160,

        /// <summary>A sort key giving more than one value for one item of the population.</summary>
        XTTE1020,

        /// <summary>A <c>group-adjacent</c> giving anything other than one atomic value.</summary>
        XTTE1100,

        /// <summary>An attribute or namespace node where only a document node could hold it.</summary>
        XTDE0420,

        /// <summary>Two namespace nodes of one name and different values on one element.</summary>
        XTDE0430,

        /// <summary>A default namespace given to an element that is in no namespace.</summary>
        XTDE0440,

        /// <summary>A parameter's written default does not match the type the parameter declared.</summary>
        XTTE0600,

        /// <summary>
        /// A parameter whose declared type does not admit the empty sequence, and whose caller left it out.
        /// </summary>
        XTDE0610,

        /// <summary>
        /// A required template parameter the caller did not supply, which from XSLT 3.0 also covers what 2.0
        /// called <see cref="XTDE0610"/>.
        /// </summary>
        XTDE0700,

        /// <summary>A required <c>static</c> parameter the caller did not supply.</summary>
        XTDE0050,


        /// <summary>A <c>zero-digit</c> that is not a digit whose value is zero.</summary>
        XTSE1295,
        /// <summary>An <c>xsl:decimal-format</c> giving one character two roles.</summary>
        XTSE1300,

        /// <summary>A <c>function-available</c> argument that is not a name.</summary>
        XTDE1400,

        /// <summary>A <c>system-property</c> argument that is not a name.</summary>
        XTDE1390,

        /// <summary>A <c>type-available</c> argument that is not a name.</summary>
        XTDE1425,

        /// <summary>An <c>element-available</c> argument that is not a name.</summary>
        XTDE1440,

        /// <summary>An extension instruction this engine does not implement, with no <c>xsl:fallback</c>.</summary>
        XTDE1450,

        /// <summary>A picture <c>fn:format-number</c> cannot read, as XSLT names that error.</summary>
        XTDE1310,

        /// <summary>A computed <c>xsl:element</c> name that is not a lexical QName.</summary>
        XTDE0820,

        /// <summary>A computed <c>xsl:element</c> name whose prefix nothing in scope binds.</summary>
        XTDE0830,

        /// <summary>An <c>xsl:element</c> whose <c>namespace</c> is not a namespace a name may be put in.</summary>
        XTDE0835,

        /// <summary>A computed <c>xsl:attribute</c> name that is not a lexical QName.</summary>
        XTDE0850,

        /// <summary>An <c>xsl:attribute</c> called <c>xmlns</c>.</summary>
        XTDE0855,

        /// <summary>A computed <c>xsl:attribute</c> name whose prefix nothing in scope binds.</summary>
        XTDE0860,

        /// <summary>An <c>xsl:attribute</c> whose <c>namespace</c> is not one a name may be put in.</summary>
        XTDE0865,

        /// <summary>An <c>xsl:processing-instruction</c> whose target is not a usable one.</summary>
        XTDE0890,

        /// <summary>An <c>xsl:namespace</c> whose value is not a URI, or is the one reserved for xmlns.</summary>
        XTDE0905,

        /// <summary>An <c>xsl:namespace</c> whose value is a zero-length string.</summary>
        XTDE0930,

        /// <summary>An <c>xsl:namespace</c> whose name is neither empty nor an XML name.</summary>
        XTDE0920,

        /// <summary>An <c>xsl:namespace</c> binding <c>xml</c> or <c>xmlns</c> to the wrong thing.</summary>
        XTDE0925,


        /// <summary>A <c>key()</c> naming a key the stylesheet does not declare.</summary>
        XTDE1260,
        /// <summary>A <c>decimal-format</c> name <c>fn:format-number</c> cannot find, as XSLT names it.</summary>
        XTDE1280,

        /// <summary>An <c>xsl:sort</c> whose <c>collation</c> names a collation this engine does not have.</summary>
        XTDE1035,

        /// <summary>Two sort key values that cannot be compared with each other.</summary>
        XTDE1030,

        /// <summary>
        /// An <c>xsl:for-each-group</c> whose <c>collation</c> names a collation this engine does not have.
        /// </summary>
        XTDE1110,

        /// <summary>An <c>xsl:key</c> whose <c>collation</c> names a collation this engine does not have.</summary>
        XTSE1210,

        /// <summary>Two <c>xsl:key</c> declarations of one name naming different collations.</summary>
        XTSE1220,

        /// <summary>An <c>xsl:import-schema</c> with both a <c>schema-location</c> and an inline schema.</summary>
        XTSE0215,

        /// <summary>The imported schemas do not make a valid schema together.</summary>
        XTSE0220,

        /// <summary>
        /// One stylesheet module says <c>input-type-annotations="strip"</c> and another
        /// <c>"preserve"</c>.
        /// </summary>
        XTSE0265,

        /// <summary>
        /// Strict validation found the document invalid: the element's content, an attribute's value or
        /// an identity constraint is not what its declaration allows.
        /// </summary>
        XTTE1510,

        /// <summary>Strict validation of an element that has no top-level declaration to validate against.</summary>
        XTTE1512,

        /// <summary>Lax validation found the document invalid, where a declaration was found for it.</summary>
        XTTE1515,

        /// <summary>Both a <c>type</c> and a <c>validation</c> attribute on one instruction or literal result element.</summary>
        XTSE1505,

        /// <summary>A <c>type</c> attribute that is not a QName, or names a type not among the schema components in scope.</summary>
        XTSE1520,

        /// <summary>A <c>type</c> naming a complex type where what is validated is an attribute.</summary>
        XTTE1535,

        /// <summary>Validation against the type a <c>type</c> attribute names found the element or attribute invalid.</summary>
        XTTE1540,

        /// <summary>An attribute validated against a type derived from <c>xs:QName</c> or <c>xs:NOTATION</c>.</summary>
        XTTE1545,

        /// <summary>A document node validated whose children are not one element with comments and processing instructions.</summary>
        XTTE1550,

        /// <summary>A document node validated whose ID, IDREF or identity constraints are not satisfied.</summary>
        XTTE1555,

        /// <summary>
        /// A template rule in a mode declared <c>typed="strict"</c> whose pattern starts with an element
        /// name the schemas in scope declare no top-level element of.
        /// </summary>
        XTSE3105,

        /// <summary>Templates applied in a mode declared <c>typed="no"</c> to a node that carries a type annotation.</summary>
        XTTE3110,

        /// <summary>A <c>regex</c> on <c>xsl:analyze-string</c> that is not a regular expression.</summary>
        XTDE1140,

        /// <summary>A <c>flags</c> on <c>xsl:analyze-string</c> holding a letter that is not a flag.</summary>
        XTDE1145,

        /// <summary>A <c>regex</c> on <c>xsl:analyze-string</c> that matches the zero-length string.</summary>
        XTDE1150,

        /// <summary>
        /// A <c>select</c> on <c>xsl:for-each-group</c> holding something that is not a node, where the
        /// grouping is by a starting or ending pattern.
        /// </summary>
        XTTE1120,

        /// <summary>A <c>format</c> naming an output definition the stylesheet does not declare.</summary>
        XTDE1460,

        /// <summary>Two final result trees written to one URI.</summary>
        XTDE1490,

        /// <summary>
        /// A <c>value</c> on <c>xsl:number</c> holding something that is not a non-negative integer.
        /// </summary>
        XTDE0980,

        /// <summary><c>xsl:copy</c> copying the context item, where there is none or it is not a node.</summary>
        XTTE0945,

        /// <summary>A copy that would leave a QName without the namespaces its prefix is bound by.</summary>
        XTTE0950,

        /// <summary><c>xsl:number</c> numbering the context item, where that is not a node.</summary>
        XTTE0990,

        /// <summary>The <c>select</c> of <c>xsl:number</c> gave something other than a single node.</summary>
        XTTE1000,

        /// <summary>A template's result does not match the <c>as</c> the template declared.</summary>
        XTTE0505,

        /// <summary>The selection of <c>xsl:apply-templates</c> held something that is not a node.</summary>
        XTTE0520,

        /// <summary>A variable or parameter's value does not match the <c>as</c> it declared.</summary>
        XTTE0570,

        /// <summary>The value supplied for a parameter does not match the <c>as</c> the parameter declared.</summary>
        XTTE0590,

        /// <summary>A function's result does not match the <c>as</c> the function declared.</summary>
        XTTE0780,

        /// <summary>A function argument does not match the <c>as</c> the parameter declared.</summary>
        XTTE0790,

        /// <summary>The principal stylesheet module does not start with a stylesheet element.</summary>
        XTSE0150,

        /// <summary>
        /// An <c>xsl:apply-templates</c> with no <c>select</c> reached with a context item that is not a
        /// node, and so has no children to process.
        /// </summary>
        XTTE0510,

        /// <summary>An <c>xsl:include</c> reaching a module that is already being read.</summary>
        XTSE0180,

        /// <summary>An <c>xsl:import</c> reaching a module that is already being read.</summary>
        XTSE0210,

        /// <summary>A prefix in a name written in the stylesheet that no namespace declaration binds.</summary>
        XTSE0280,

        /// <summary>An attribute value template with an unclosed <c>{</c>.</summary>
        XTSE0350,

        /// <summary>An attribute value template with a <c>}</c> that is not doubled.</summary>
        XTSE0370,

        /// <summary>A <c>priority</c> that is not a number.</summary>
        XTSE0530,

        /// <summary>Two templates of one name at one import precedence.</summary>
        XTSE0660,

        /// <summary>An <c>xsl:call-template</c> leaving a required parameter unsupplied.</summary>
        XTSE0690,

        /// <summary>An attribute set that uses itself, directly or through others.</summary>
        XTSE0720,

        /// <summary>An <c>xsl:function</c> whose name is in no namespace.</summary>
        XTSE0740,

        /// <summary>A prefix on <c>xsl:namespace-alias</c> that no namespace declaration binds.</summary>
        XTSE0812,

        /// <summary>An <c>xsl:for-each-group</c> saying how to group in no way, or in two.</summary>
        XTSE1080,

        /// <summary>Two character maps of one name at one import precedence.</summary>
        XTSE1580,

        /// <summary>A character map that uses itself, directly or through others.</summary>
        XTSE1600,

        /// <summary>An attribute added to an element whose content has already started.</summary>
        XTDE0410,

        /// <summary>A type error: an operand or argument of the wrong type, or of the wrong cardinality.</summary>
        XPTY0004,


        /// <summary>The last step of a path gave both nodes and atomic values.</summary>
        XPTY0018,

        /// <summary>A step in a path applied to something that is not a node.</summary>
        XPTY0019,

        /// <summary>An axis step whose context item is not a node.</summary>
        XPTY0020,

        /// <summary>Untyped content where <c>xs:QName</c> or <c>xs:NOTATION</c> is declared.</summary>
        XPTY0117,

        /// <summary>An expression that does not fit the grammar.</summary>
        XPST0003,

        /// <summary>A variable that is not in scope.</summary>
        XPST0008,

        /// <summary>An axis the implementation does not support.</summary>
        XPST0010,

        /// <summary>An unknown function, or a known one called with a number of arguments it does not take.</summary>
        XPST0017,

        /// <summary>A type name that is not defined.</summary>
        XPST0051,

        /// <summary>A cast naming a type that is not one a value can be cast to.</summary>
        XPST0080,

        /// <summary>A namespace prefix that is not bound.</summary>
        XPST0081,

        /// <summary>An expression needs a context item and there is none.</summary>
        XPDY0002,

        /// <summary>A value asserted by <c>treat as</c> to be of a type it is not.</summary>
        XPDY0050,

        /// <summary>
        /// A limit this implementation sets rather than the language, which XPath 3.0 gives a code of its
        /// own so that a stylesheet can tell "too big for this processor" from "wrong".
        /// </summary>
        XPDY0130,

        /// <summary>Division by zero, in a type where that has no value.</summary>
        FOAR0001,

        /// <summary>A numeric operation overflowed or underflowed the range of its type.</summary>
        FOAR0002,

        /// <summary>A value too large to hold as an <c>xs:decimal</c>.</summary>
        FOCA0001,

        /// <summary>A value that cannot be cast to the type asked for.</summary>
        FOCA0002,

        /// <summary>A value too large to hold as an <c>xs:integer</c>.</summary>
        FOCA0003,

        /// <summary>A duration scaled by something that is not a number.</summary>
        FOCA0005,

        /// <summary>A code point that is not a legal XML character.</summary>
        FOCH0001,

        /// <summary>A collation the processor does not have.</summary>
        FOCH0002,

        /// <summary>A Unicode normalization form the processor does not support.</summary>
        FOCH0003,

        /// <summary>
        /// A collation that cannot do what was asked of it: match a substring, or make a collation key.
        /// </summary>
        FOCH0004,

        /// <summary>A prefix with no binding to resolve it against.</summary>
        FONS0004,

        /// <summary>A relative URI with no base URI to resolve it against.</summary>
        FONS0005,

        /// <summary>An <c>id()</c> or <c>idref()</c> asked about a node that is not in a document.</summary>
        FODC0001,

        /// <summary>A document or collection that could not be retrieved, or that is not XML.</summary>
        FODC0002,

        /// <summary>A collection was asked for with no argument, and there is no default one.</summary>
        FODC0003,

        /// <summary>A collection was asked for by something that is not a URI at all.</summary>
        FODC0004,

        /// <summary>Text that could not be retrieved, or that is not in the encoding claimed for it.</summary>
        FOUT1170,

        /// <summary>Text that decoded to characters XML does not permit.</summary>
        FOUT1190,

        /// <summary>A date or time outside the range moments are held in.</summary>
        FODT0001,

        /// <summary>A duration operation whose result is outside the range durations are held in.</summary>
        FODT0002,

        /// <summary>A value offered as a timezone that is not one.</summary>
        FODT0003,

        /// <summary>A picture string that does not parse.</summary>
        FOFD1340,

        /// <summary>A picture string naming a component the value does not have.</summary>
        FOFD1350,

        /// <summary><c>fn:error()</c> was called without naming an error of its own.</summary>
        FOER0000,

        /// <summary>A value that is not in the lexical space of the type it is being cast to.</summary>
        FORG0001,

        /// <summary>A URI that is not a reference at all, or a base URI that cannot be resolved against.</summary>
        FORG0002,

        /// <summary><c>zero-or-one()</c> was given more than one item.</summary>
        FORG0003,

        /// <summary><c>one-or-more()</c> was given an empty sequence.</summary>
        FORG0004,

        /// <summary><c>exactly-one()</c> was given anything other than one item.</summary>
        FORG0005,

        /// <summary>An argument of a type the function does not accept.</summary>
        FORG0006,

        /// <summary>Two arguments that carry inconsistent timezones.</summary>
        FORG0008,

        /// <summary>Text that <c>fn:parse-ietf-date()</c> cannot read as a date.</summary>
        FORG0010,

        /// <summary>An invalid flag on a regular expression.</summary>
        FORX0001,

        /// <summary>A regular expression that will not compile.</summary>
        FORX0002,

        /// <summary>A regular expression that matches the empty string, where that would not terminate.</summary>
        FORX0003,

        /// <summary>A replacement string that is not one <c>fn:replace</c> allows.</summary>
        FORX0004,

        /// <summary>
        /// Atomizing an element whose type has element-only content, which has no typed value to give.
        /// </summary>
        FOTY0012,

        /// <summary>Atomizing a function item, which has no typed value to give.</summary>
        FOTY0013,

        /// <summary>
        /// Asking a function item for its string-value, which it has not.
        /// </summary>
        /// <remarks>
        /// The two codes divide by the question asked rather than by what was asked: <c>fn:data</c> of a
        /// function is <c>FOTY0013</c> and <c>fn:string</c> of the same function is this one.
        /// </remarks>
        FOTY0014,

        /// <summary>Comparing function items, which <c>fn:deep-equal</c> cannot do.</summary>
        FOTY0015,

        /// <summary>A position outside an array.</summary>
        FOAY0001,

        /// <summary>A negative length for <c>array:subarray</c>.</summary>
        FOAY0002,

        /// <summary>An array of arguments <c>fn:apply</c> cannot fit the function it was given.</summary>
        FOAP0001,

        /// <summary>A transformation <c>fn:transform</c> was asked for that this processor cannot run.</summary>
        FOXT0001,

        /// <summary>A stylesheet or package <c>fn:transform</c> could not retrieve.</summary>
        FOXT0002,

        /// <summary>An option given to <c>fn:transform</c> that is not one it defines, or is of the wrong type.</summary>
        FOXT0004,

        /// <summary>Text that <c>fn:parse-xml</c> could not read as XML.</summary>
        FODC0006,

        /// <summary>A decimal format named by <c>fn:format-number</c> that nothing declared.</summary>
        FODF1280,

        /// <summary>A picture string <c>fn:format-number</c> cannot read.</summary>
        FODF1310,

        /// <summary>Text that is not well-formed JSON.</summary>
        FOJS0001,

        /// <summary>A key that <c>map:merge</c> or <c>fn:parse-json</c> was told to reject appearing twice.</summary>
        FOJS0003,

        /// <summary>
        /// A key written twice in a map constructor, <c>map { 'a': 1, 'a': 2 }</c>, which is the same shape
        /// as <see cref="FOJS0003"/> arrived at a third way and has a code of its own.
        /// </summary>
        XQDY0137,

        /// <summary>A JSON feature this processor does not support.</summary>
        FOJS0004,

        /// <summary>An option given to <c>map:merge</c> or one of the JSON functions that is not one it defines.</summary>
        FOJS0005,

        /// <summary>A node that is not a valid XML representation of JSON.</summary>
        FOJS0006,

        /// <summary>A backslash escape that JSON does not define.</summary>
        FOJS0007,

        /// <summary>A node with no serialized form of its own, such as a free-standing attribute.</summary>
        SENR0001,

        /// <summary>A serialization parameter document this processor cannot use.</summary>
        SEPM0017,

        /// <summary>A serialization parameter given a value it may not take.</summary>
        SEPM0016,

        /// <summary>Two mappings for one character in a character map.</summary>
        SEPM0018,

        /// <summary>A serialization parameter set twice.</summary>
        SEPM0019,

        /// <summary>A value the JSON output method cannot write, such as infinity.</summary>
        SERE0020,

        /// <summary>A function item handed to the JSON output method.</summary>
        SERE0021,

        /// <summary>A key repeated once a map's keys are written as JSON field names.</summary>
        SERE0022,

        /// <summary>A sequence where the JSON output method takes a single value.</summary>
        SERE0023,
    }

    /// <summary>Helpers for raising errors that the specifications name.</summary>
    public static class XsltErrors
    {
        /// <summary>Creates an exception carrying a specification error code.</summary>
        /// <param name="code">The code.</param>
        /// <param name="message">A description of the error, for whoever has to fix it.</param>
        public static XsltException Error(XsltErrorCode code, string message)
        {
            return new XsltException(code, message);
        }

        /// <summary>Creates an exception carrying a specification error code and an underlying cause.</summary>
        /// <param name="code">The code.</param>
        /// <param name="message">A description of the error, for whoever has to fix it.</param>
        /// <param name="innerException">
        /// What the framework said, kept for diagnosis. It is not what the message says: the framework can
        /// only report an argument it would not accept, where the message names the rule that was broken.
        /// </param>
        public static XsltException Error(XsltErrorCode code, string message, Exception innerException)
        {
            return new XsltException(message, innerException) { Code = code.ToString() };
        }
    }
}
