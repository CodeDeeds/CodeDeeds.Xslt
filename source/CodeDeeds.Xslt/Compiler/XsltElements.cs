namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>Where an XSLT element is allowed to appear.</summary>
    [Flags]
    internal enum XsltPlacement : byte
    {
        /// <summary>A declaration: a child of <c>xsl:stylesheet</c>.</summary>
        Declaration = 1,

        /// <summary>An instruction: anywhere a sequence constructor may appear.</summary>
        Instruction = 2,

        /// <summary>Neither: it belongs only inside the elements named in <see cref="XsltElement.Parents"/>.</summary>
        Subordinate = 4,
    }

    /// <summary>What the specification allows on one XSLT element.</summary>
    internal sealed class XsltElement
    {
        public XsltPlacement Placement { get; init; }

        /// <summary>Attributes that must be written.</summary>
        public string[] Required { get; init; } = Array.Empty<string>();

        /// <summary>Attributes that may be written.</summary>
        public string[] Optional { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Attributes whose value must be a QName written out, rather than computed.
        /// </summary>
        /// <remarks>
        /// The distinction matters: <c>xsl:element name="{$x}"</c> is an attribute value template and
        /// <c>xsl:variable name="{$x}"</c> is a variable whose name is the eleven characters <c>{$x}</c>,
        /// which is not a name at all.
        /// </remarks>
        public string[] Names { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Whether the <c>name</c> attribute declares a name rather than referring to one.
        /// </summary>
        /// <remarks>
        /// Only a declaration can put a name in a namespace, so only a declaration can put one in a namespace
        /// it may not use. <c>xsl:call-template name="xs:f"</c> names a template that cannot exist, which the
        /// declaration side has already refused.
        /// </remarks>
        public bool Declares { get; init; }

        /// <summary>The elements this one may appear inside, where <see cref="XsltPlacement.Subordinate"/>.</summary>
        public string[] Parents { get; init; } = Array.Empty<string>();

        /// <summary>
        /// The code for this element standing where it may not, where the specification gives it one of its
        /// own rather than the general <c>XTSE0010</c>.
        /// </summary>
        public XsltErrorCode? Misplaced { get; init; }

        /// <summary>
        /// The only XSLT elements this one may hold, or null where it holds a sequence constructor.
        /// </summary>
        /// <remarks>
        /// An empty array means an element that holds nothing at all. Null and empty are therefore quite
        /// different here: <c>xsl:copy</c> holds whatever a stylesheet writes in it, and <c>xsl:copy-of</c>
        /// holds nothing.
        /// </remarks>
        public string[]? Children { get; init; }

        /// <summary>
        /// Elements that may appear only before any other content.
        /// </summary>
        /// <remarks>
        /// <c>xsl:param</c> at the top of a template and <c>xsl:sort</c> at the top of an
        /// <c>xsl:for-each</c>. Both would otherwise be read as part of the content and quietly do nothing
        /// where they stand — a parameter declared after the first instruction is a parameter that was never
        /// declared.
        /// </remarks>
        public string[] Leading { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Whether the parent's content model allows only one of this element.
        /// </summary>
        /// <remarks>
        /// <c>xsl:context-item</c> and <c>xsl:on-completion</c>, each of which says one thing about the
        /// whole of the element it stands in. Two of either would be two answers to one question, and the
        /// specification refuses the pair rather than settling which of them is heard.
        /// </remarks>
        public bool Once { get; init; }

        /// <summary>
        /// Elements that may appear only after all other content: once where the element is marked
        /// <see cref="Once"/>, and otherwise as many times as the stylesheet writes them.
        /// </summary>
        /// <remarks>
        /// <c>xsl:otherwise</c>, which is what an <c>xsl:choose</c> does when nothing else has. Written
        /// before an <c>xsl:when</c> it would be tested first and always taken, so the specification puts it
        /// last rather than leaving a stylesheet to be read two ways. And the <c>xsl:fallback*</c> that
        /// closes an <c>xsl:merge</c> or an <c>xsl:analyze-string</c>, of which there may be several.
        /// </remarks>
        public string[] Trailing { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Whether text is content this element holds.
        /// </summary>
        /// <remarks>
        /// True only for <c>xsl:text</c>, which holds text and nothing else. It is therefore not an element
        /// <em>required to be empty</em>, so content it may not hold is the general <c>XTSE0010</c> rather
        /// than the <c>XTSE0260</c> the specification names for emptiness alone.
        /// </remarks>
        public bool Text { get; init; }

        /// <summary>
        /// The earliest version of XSLT that defines this element.
        /// </summary>
        /// <remarks>
        /// What keeps a 3.0 element out of a 2.0 stylesheet. Implementing one ahead of the version claim is
        /// otherwise a way of quietly changing what a <c>version="2.0"</c> stylesheet means: <c>xsl:try</c>
        /// there is an element the specification does not define, and a stylesheet that wrote an
        /// <c>xsl:fallback</c> for exactly that reason is entitled to have it taken.
        /// </remarks>
        public XsltVersion Since { get; init; } = XsltVersion.V10;

        /// <summary>
        /// The attributes in this element's lists that XSLT 3.0 added.
        /// </summary>
        /// <remarks>
        /// The same rule <see cref="Since"/> states for a whole element, applied to an element that already
        /// existed and grew an attribute. It has to be per element rather than a list of names, because one
        /// name can be old on one element and new on another: <c>select</c> has been on <c>xsl:value-of</c>
        /// since 1.0 and arrived on <c>xsl:copy</c> in 3.0.
        /// </remarks>
        public string[] Since30 { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Whether this element declares a <em>component</em>, which XSLT 3.0 lets a package give a
        /// visibility.
        /// </summary>
        /// <remarks>
        /// The seven kinds a package can expose or keep to itself. It is a flag rather than
        /// <c>visibility</c> in each element's optional list because the answer is the same for all of them
        /// and being a component is the reason, not a coincidence of seven tables agreeing.
        /// </remarks>
        public bool Visible { get; init; }
    }

    /// <summary>
    /// The shape of every XSLT element: where it belongs, what it must carry, and what it may carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table rather than a check spread through the compiler, because the questions it answers are the
    /// same three questions for every element and the specification states them in one table of its own.
    /// Written out here, a stylesheet that misspells an attribute hears about it in the words the
    /// specification uses, instead of being compiled as though the attribute had not been written.
    /// </para>
    /// <para>
    /// It covers XSLT 2.0, which is what this engine implements. An element from a later version is not in
    /// it and is refused — unless forwards-compatible processing is in force, where being written for a
    /// version this engine does not have is the whole point.
    /// </para>
    /// </remarks>
    internal static class XsltElements
    {
        /// <summary>
        /// The attributes any XSLT element may carry.
        /// </summary>
        /// <remarks>
        /// On an XSLT element they are written without a prefix; on a literal result element they carry the
        /// <c>xsl:</c> prefix. Which is why <c>xsl:xpath-default-namespace</c> on an XSLT element is an
        /// error rather than a long-winded way of writing the same thing.
        /// </remarks>
        public static readonly string[] Standard =
        {
            "version",
            "exclude-result-prefixes",
            "extension-element-prefixes",
            "xpath-default-namespace",
            "default-collation",
            "use-when",
            "expand-text",
            "default-mode",
        };

        /// <summary>
        /// The attributes a literal result element may carry in the XSLT namespace.
        /// </summary>
        /// <remarks>
        /// The standard ones, and the four that only make sense where an element is being produced rather
        /// than a stylesheet read. Anything else there is a stylesheet addressing the processor in words it
        /// does not have: the namespace is reserved, so the attribute cannot be data either.
        /// </remarks>
        public static readonly string[] OnResultElement =
        {
            "version",
            "exclude-result-prefixes",
            "extension-element-prefixes",
            "xpath-default-namespace",
            "default-collation",
            "use-when",
            "expand-text",
            "default-mode",
            "use-attribute-sets",
            "inherit-namespaces",
            "validation",
            "type",
        };

        /// <summary>
        /// The namespaces a stylesheet may not declare a name of its own in.
        /// </summary>
        /// <remarks>
        /// They belong to the specifications, which is what lets <c>xs:date</c> and <c>fn:substring</c> mean
        /// one thing everywhere. A stylesheet function called <c>fn:substring</c> would either shadow the
        /// standard one or be shadowed by it, and neither reading is written down anywhere.
        /// </remarks>
        public static readonly string[] Reserved =
        {
            "http://www.w3.org/XML/1998/namespace",
            "http://www.w3.org/2000/xmlns/",
            "http://www.w3.org/1999/XSL/Transform",
            "http://www.w3.org/2001/XMLSchema",
            "http://www.w3.org/2001/XMLSchema-instance",
            "http://www.w3.org/2005/xpath-functions",
            "http://www.w3.org/2005/xpath-functions/math",
            "http://www.w3.org/2005/xpath-functions/map",
            "http://www.w3.org/2005/xpath-functions/array",
            "http://www.w3.org/2005/xqt-errors",
        };

        /// <summary>What a boolean attribute may say in XSLT 1.0 and 2.0.</summary>
        public static readonly string[] YesNo = { "yes", "no" };

        /// <summary>
        /// What a boolean attribute may say in XSLT 3.0, which widened every one of them to the six
        /// spellings <c>xs:boolean</c> has.
        /// </summary>
        public static readonly string[] BooleanValues = { "yes", "no", "true", "false", "1", "0" };

        /// <summary>
        /// What <c>standalone</c> may say in XSLT 1.0 and 2.0.
        /// </summary>
        /// <remarks>
        /// Not a boolean, because the third value is not a third truth: <c>omit</c> leaves the declaration
        /// without a standalone at all, which is a different document from one that says it is not
        /// standalone. It therefore needs a widened list of its own rather than sharing the boolean one.
        /// </remarks>
        public static readonly string[] StandaloneValues = { "yes", "no", "omit" };

        /// <summary>What <c>standalone</c> may say in XSLT 3.0, which widened the two truths and not the third.</summary>
        public static readonly string[] StandaloneValues30 =
        {
            "yes", "no", "true", "false", "1", "0", "omit",
        };

        /// <summary>An element that holds nothing at all, as against one that holds a sequence constructor.</summary>
        private static readonly string[] Empty = Array.Empty<string>();

        /// <summary>
        /// The values an attribute is restricted to, by attribute name.
        /// </summary>
        /// <remarks>
        /// Keyed on the name alone, which is enough because the specification does not give one name two
        /// meanings: <c>order</c> is ascending or descending wherever it appears. Several of these are
        /// attribute value templates, so a value holding a curly brace is left for the run to decide.
        /// </remarks>
        private static readonly Dictionary<string, string[]> s_values = new(StringComparer.Ordinal)
        {
            ["disable-output-escaping"] = YesNo,
            ["terminate"] = YesNo,
            ["stable"] = YesNo,
            ["required"] = YesNo,
            ["tunnel"] = YesNo,
            ["static"] = YesNo,
            ["warning-on-no-match"] = YesNo,
            ["warning-on-multiple-match"] = YesNo,
            ["typed"] = new[] { "yes", "no", "true", "false", "1", "0", "strict", "lax", "unspecified" },
            ["on-no-match"] = new[]
            {
                "deep-copy", "shallow-copy", "deep-skip", "shallow-skip", "text-only-copy", "fail",
            },
            ["phase"] = new[] { "start", "end" },
            ["visibility"] = new[] { "public", "private", "final", "abstract", "hidden" },
            ["on-multiple-match"] = new[] { "use-last", "fail" },
            ["override"] = YesNo,
            ["override-extension-function"] = BooleanValues,
            ["cache"] = BooleanValues,
            ["streamable"] = BooleanValues,
            ["new-each-time"] = new[] { "yes", "no", "true", "false", "1", "0", "maybe" },
            ["streamability"] = new[]
            {
                "unclassified", "absorbing", "inspection", "filter", "shallow-descent", "deep-descent",
                "ascent",
            },
            ["composite"] = BooleanValues,
            ["schema-aware"] = YesNo,
            ["copy-namespaces"] = YesNo,
            ["inherit-namespaces"] = YesNo,
            ["indent"] = YesNo,
            ["omit-xml-declaration"] = YesNo,
            ["byte-order-mark"] = YesNo,
            ["escape-uri-attributes"] = YesNo,
            ["include-content-type"] = YesNo,
            ["undeclare-prefixes"] = YesNo,
            ["standalone"] = StandaloneValues,
            ["build-tree"] = BooleanValues,
            ["order"] = new[] { "ascending", "descending" },
            ["case-order"] = new[] { "upper-first", "lower-first" },
            ["level"] = new[] { "single", "multiple", "any" },
            ["letter-value"] = new[] { "alphabetic", "traditional" },
            ["validation"] = new[] { "strict", "lax", "preserve", "strip" },
            ["default-validation"] = new[] { "preserve", "strip" },
            ["input-type-annotations"] = new[] { "unspecified", "preserve", "strip" },
        };

        private static readonly Dictionary<string, XsltElement> s_elements = new(StringComparer.Ordinal)
        {
            // ---- The stylesheet module itself ------------------------------------------------------------
            ["stylesheet"] = new XsltElement
            {
                Placement = XsltPlacement.Subordinate,
                Required = new[] { "version" },
                Optional = new[]
                {
                    "id", "extension-element-prefixes", "exclude-result-prefixes", "xpath-default-namespace",
                    "default-validation", "default-collation", "input-type-annotations",
                },
            },
            ["package"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,
                Required = new[] { "version" },
                Optional = new[]
                {
                    "id", "name", "package-version", "declared-modes",
                    "extension-element-prefixes", "exclude-result-prefixes", "xpath-default-namespace",
                    "default-validation", "default-collation", "input-type-annotations",
                },
            },
            ["use-package"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Declaration,
                Required = new[] { "name" },
                Optional = new[] { "package-version" },
                Children = new[] { "accept", "override" },
            },
            ["accumulator"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Declaration,
                Visible = true,
                Required = new[] { "name", "initial-value" },
                Optional = new[] { "as", "streamable" },
                Names = new[] { "name" },
                Declares = true,
                Children = new[] { "accumulator-rule" },
            },
            ["accumulator-rule"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,
                Required = new[] { "match" },
                Optional = new[] { "select", "phase" },
                Parents = new[] { "accumulator" },
            },
            ["expose"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Declaration,
                Required = new[] { "component", "names", "visibility" },
                Children = Array.Empty<string>(),
            },
            ["accept"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,
                Required = new[] { "component", "names", "visibility" },
                Parents = new[] { "use-package" },
                Children = Array.Empty<string>(),
            },
            ["override"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,
                Parents = new[] { "use-package" },
            },

            // The same element under a second name, which XSLT 1.0 provided and never chose between.
            ["transform"] = new XsltElement
            {
                Placement = XsltPlacement.Subordinate,
                Required = new[] { "version" },
                Optional = new[]
                {
                    "id", "extension-element-prefixes", "exclude-result-prefixes", "xpath-default-namespace",
                    "default-validation", "default-collation", "input-type-annotations",
                },
            },

            // ---- Declarations ----------------------------------------------------------------------------
            ["import"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Required = new[] { "href" },
                Children = Empty,
                Misplaced = XsltErrorCode.XTSE0190,
            },
            ["include"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Required = new[] { "href" },
                Children = Empty,
                Misplaced = XsltErrorCode.XTSE0170,
            },
            ["template"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Visible = true,
                Optional = new[] { "match", "name", "priority", "mode", "as" },
                Names = new[] { "name" },
                Declares = true,
                Leading = new[] { "context-item", "param" },
            },
            ["where-populated"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
            },
            ["assert"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Required = new[] { "test" },
                Optional = new[] { "select", "error-code" },
            },
            ["fork"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
            },
            ["source-document"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Required = new[] { "href" },
                Optional = new[] { "streamable", "validation", "type", "use-accumulators" },
            },
            ["merge"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,

                // The content model is (merge-source+, merge-action, fallback*): a fallback is admitted
                // after the action, for a processor that does not have xsl:merge, and ignored by one that
                // does. After, not before: the order is part of the model.
                Children = new[] { "merge-source", "merge-action", "fallback" },
                Trailing = new[] { "fallback" },
            },
            ["merge-source"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,
                Optional = new[]
                {
                    "name", "for-each-item", "for-each-source", "for-each-stream", "select",
                    "sort-before-merge", "streamable", "validation", "type", "use-accumulators",
                },
                Names = new[] { "name" },
                Parents = new[] { "merge" },
                Children = new[] { "merge-key" },
            },
            ["merge-key"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,

                // The attributes of xsl:sort but for 'stable': a merge is not a sort, and there is nothing
                // for stability to be a property of.
                Optional = new[] { "select", "lang", "order", "collation", "case-order", "data-type" },
                Parents = new[] { "merge-source" },
            },
            ["merge-action"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,
                Parents = new[] { "merge" },
            },
            ["map"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
            },
            ["map-entry"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Required = new[] { "key" },
                Optional = new[] { "select" },
            },
            ["on-empty"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select" },
            },
            ["on-non-empty"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select" },
            },
            ["evaluate"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Required = new[] { "xpath" },
                Optional = new[] { "as", "base-uri", "with-params", "context-item", "namespace-context", "schema-aware" },
                Children = new[] { "with-param", "fallback" },
            },
            ["iterate"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Required = new[] { "select" },
                Leading = new[] { "param", "on-completion" },
            },
            ["next-iteration"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Children = new[] { "with-param" },
            },
            ["break"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select" },
            },
            ["on-completion"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,
                Once = true,
                Optional = new[] { "select" },
                Parents = new[] { "iterate" },
            },
            ["try"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select", "rollback-output" },
                Trailing = new[] { "catch" },
            },
            ["catch"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,
                Optional = new[] { "errors", "select" },
                Parents = new[] { "try" },
            },
            ["context-item"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Subordinate,
                Once = true,
                Optional = new[] { "as", "use" },
                Parents = new[] { "template" },
                Children = Array.Empty<string>(),
            },
            ["global-context-item"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Declaration,
                Optional = new[] { "as", "use" },
                Children = Array.Empty<string>(),
            },
            ["variable"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration | XsltPlacement.Instruction,
                Visible = true,
                Required = new[] { "name" },
                Optional = new[] { "select", "as", "static" },
                Names = new[] { "name" },
                Declares = true,
            },
            // No visibility: a stylesheet parameter is a component of its package like any other, but what
            // it is visible as is said by an xsl:expose rather than on the declaration (§3.5.2). Writing it
            // here is writing an attribute the element does not have.
            ["param"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration | XsltPlacement.Subordinate,
                Required = new[] { "name" },
                Optional = new[] { "select", "as", "required", "tunnel", "static" },
                Names = new[] { "name" },
                Declares = true,
                Parents = new[] { "template", "function", "stylesheet", "transform" },
            },
            ["mode"] = new XsltElement
            {
                Since = XsltVersion.V30,
                Placement = XsltPlacement.Declaration,
                Visible = true,
                Optional = new[]
                {
                    "name", "on-no-match", "on-multiple-match", "warning-on-no-match",
                    "warning-on-multiple-match", "typed", "visibility", "streamable", "use-accumulators",
                },
                Names = new[] { "name" },
                Declares = true,
                Children = Array.Empty<string>(),
            },
            ["output"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Optional = new[]
                {
                    "name", "method", "byte-order-mark", "cdata-section-elements", "doctype-public",
                    "doctype-system", "encoding", "escape-uri-attributes", "include-content-type", "indent",
                    "media-type", "normalization-form", "omit-xml-declaration", "standalone",
                    "undeclare-prefixes", "use-character-maps", "version",
                    "html-version", "item-separator", "suppress-indentation", "parameter-document",
                    "build-tree", "json-node-output-method", "allow-duplicate-names",
                },
                Since30 = new[]
                {
                    "html-version", "item-separator", "suppress-indentation", "parameter-document",
                    "build-tree", "json-node-output-method", "allow-duplicate-names",
                },
                Names = new[] { "name" },
                Declares = true,
                Children = Empty,
            },
            ["key"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Visible = true,
                Required = new[] { "name", "match" },
                Optional = new[] { "use", "collation", "composite" },
                Since30 = new[] { "composite" },
                Names = new[] { "name" },
                Declares = true,
            },
            ["decimal-format"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Optional = new[]
                {
                    "name", "decimal-separator", "grouping-separator", "infinity", "minus-sign", "NaN",
                    "percent", "per-mille", "zero-digit", "digit", "pattern-separator",
                    "exponent-separator",
                },
                Since30 = new[] { "exponent-separator" },
                Names = new[] { "name" },
                Declares = true,
                Children = Empty,
            },
            ["strip-space"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Required = new[] { "elements" },
                Children = Empty,
            },
            ["preserve-space"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Required = new[] { "elements" },
                Children = Empty,
            },
            ["attribute-set"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Visible = true,
                Required = new[] { "name" },
                Optional = new[] { "use-attribute-sets", "streamable" },
                Since30 = new[] { "streamable" },
                Names = new[] { "name" },
                Declares = true,
                Children = new[] { "attribute" },
            },
            ["namespace-alias"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Required = new[] { "stylesheet-prefix", "result-prefix" },
                Children = Empty,
            },
            ["function"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Visible = true,
                Required = new[] { "name" },
                Optional = new[]
                {
                    "as", "override", "override-extension-function", "new-each-time", "cache", "streamability",
                },
                Since30 = new[] { "override-extension-function", "new-each-time", "cache", "streamability" },
                Names = new[] { "name" },
                Declares = true,
                Leading = new[] { "param" },
            },
            ["character-map"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Required = new[] { "name" },
                Optional = new[] { "use-character-maps" },
                Names = new[] { "name" },
                Declares = true,
                Children = new[] { "output-character" },
            },
            ["import-schema"] = new XsltElement
            {
                Placement = XsltPlacement.Declaration,
                Optional = new[] { "namespace", "schema-location" },
            },

            // ---- Instructions ----------------------------------------------------------------------------
            ["apply-templates"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select", "mode" },
                Children = new[] { "sort", "with-param" },
            },
            ["apply-imports"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Children = new[] { "with-param" },
            },
            ["next-match"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Children = new[] { "with-param", "fallback" },
            },
            ["call-template"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "name" },
                Names = new[] { "name" },
                Children = new[] { "with-param" },
            },
            ["value-of"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select", "separator", "disable-output-escaping" },
            },
            ["text"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "disable-output-escaping" },
                Children = Empty,
                Text = true,
            },
            ["copy-of"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "select" },
                Optional = new[] { "copy-namespaces", "validation", "type", "copy-accumulators" },
                Children = Empty,
                Since30 = new[] { "copy-accumulators" },
            },
            ["copy"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[]
                {
                    "copy-namespaces", "inherit-namespaces", "use-attribute-sets", "validation", "type",
                    "select", "copy-accumulators",
                },
                Since30 = new[] { "select", "copy-accumulators" },
            },
            ["sequence"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select" },
            },
            ["for-each"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "select" },
                Leading = new[] { "sort" },
            },
            ["for-each-group"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "select" },
                Optional = new[]
                {
                    "group-by", "group-adjacent", "group-starting-with", "group-ending-with", "collation",
                    "composite",
                },
                Since30 = new[] { "composite" },
                Leading = new[] { "sort" },
            },
            ["if"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "test" },
            },
            ["choose"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Children = new[] { "when", "otherwise" },
                Trailing = new[] { "otherwise" },
            },
            ["perform-sort"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select" },
                Leading = new[] { "sort" },
            },
            ["element"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "name" },
                Optional = new[] { "namespace", "inherit-namespaces", "use-attribute-sets", "validation", "type" },
            },
            ["attribute"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "name" },
                Optional = new[] { "namespace", "select", "separator", "validation", "type" },
            },
            ["namespace"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "name" },
                Optional = new[] { "select" },
            },
            ["comment"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select" },
            },
            ["processing-instruction"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "name" },
                Optional = new[] { "select" },
            },
            ["document"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "validation", "type" },
            },
            ["number"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[]
                {
                    "value", "select", "level", "count", "from", "format", "lang", "letter-value", "ordinal",
                    "grouping-separator", "grouping-size", "start-at",
                },
                Since30 = new[] { "start-at" },
                Children = Empty,
            },
            ["message"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[] { "select", "terminate", "error-code" },
                Since30 = new[] { "error-code" },
            },
            ["analyze-string"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Required = new[] { "select", "regex" },
                Optional = new[] { "flags" },
                Children = new[] { "matching-substring", "non-matching-substring", "fallback" },
                Trailing = new[] { "fallback" },
            },
            ["result-document"] = new XsltElement
            {
                Placement = XsltPlacement.Instruction,
                Optional = new[]
                {
                    "format", "href", "validation", "type", "method", "byte-order-mark",
                    "cdata-section-elements", "doctype-public", "doctype-system", "encoding",
                    "escape-uri-attributes", "include-content-type", "indent", "media-type",
                    "normalization-form", "omit-xml-declaration", "output-version", "standalone",
                    "undeclare-prefixes", "use-character-maps",
                    "html-version", "item-separator", "suppress-indentation", "parameter-document",
                    "build-tree", "json-node-output-method", "allow-duplicate-names",
                },
                Since30 = new[]
                {
                    "html-version", "item-separator", "suppress-indentation", "parameter-document",
                    "build-tree",
                },
            },

            // ---- Elements that belong to one particular parent --------------------------------------------
            ["sort"] = new XsltElement
            {
                Placement = XsltPlacement.Subordinate,
                Optional = new[] { "select", "lang", "data-type", "order", "case-order", "collation", "stable" },
                Parents = new[] { "apply-templates", "for-each", "for-each-group", "perform-sort" },
            },
            ["with-param"] = new XsltElement
            {
                Placement = XsltPlacement.Subordinate,
                Required = new[] { "name" },
                Optional = new[] { "select", "as", "tunnel" },
                Names = new[] { "name" },
                Parents = new[] { "apply-templates", "call-template", "apply-imports", "next-match", "evaluate" },
            },
            ["when"] = new XsltElement
            {
                Placement = XsltPlacement.Subordinate,
                Required = new[] { "test" },
                Parents = new[] { "choose" },
            },
            ["otherwise"] = new XsltElement
            {
                Placement = XsltPlacement.Subordinate,
                Parents = new[] { "choose" },

                // The one trailing element the model has only one of: a second would be a second answer to
                // what a choose does when nothing else has.
                Once = true,
            },
            ["matching-substring"] = new XsltElement
            {
                Placement = XsltPlacement.Subordinate,
                Parents = new[] { "analyze-string" },
            },
            ["non-matching-substring"] = new XsltElement
            {
                Placement = XsltPlacement.Subordinate,
                Parents = new[] { "analyze-string" },
            },
            ["output-character"] = new XsltElement
            {
                Placement = XsltPlacement.Subordinate,
                Required = new[] { "character", "string" },
                Parents = new[] { "character-map" },
                Children = Empty,
            },

            // xsl:fallback belongs inside any instruction, including ones from a later version that this
            // engine has never heard of — which is the whole point of it — so it names no parents.
            ["fallback"] = new XsltElement { Placement = XsltPlacement.Instruction },
        };

        /// <summary>
        /// Whether a name in a reserved namespace is one the specification defines there itself.
        /// </summary>
        /// <remarks>
        /// The XSLT namespace is reserved so that a stylesheet cannot invent names in it — but XSLT itself
        /// puts a handful there for a stylesheet to declare, <c>xsl:initial-template</c> being the entry
        /// point a caller can name without knowing the stylesheet's prefixes. Those are exactly the names a
        /// stylesheet is meant to write, so refusing them would refuse the mechanism they exist for.
        /// </remarks>
        public static bool IsDefinedName(string namespaceUri, string localName)
        {
            return namespaceUri == "http://www.w3.org/1999/XSL/Transform"
                && localName is "initial-template" or "original" or "unnamed" or "default" or "all";
        }

        /// <summary>Looks up what the specification allows on an element.</summary>
        public static XsltElement? Find(string localName)
        {
            return s_elements.TryGetValue(localName, out XsltElement? shape) ? shape : null;
        }

        /// <summary>The values an attribute is restricted to, or null where it is free.</summary>
        public static string[]? ValuesOf(string attribute)
        {
            return s_values.TryGetValue(attribute, out string[]? values) ? values : null;
        }

        /// <summary>Whether an element may carry an attribute, standard attributes included.</summary>
        public static bool Allows(XsltElement shape, string attribute)
        {
            return Array.IndexOf(shape.Required, attribute) >= 0
                || Array.IndexOf(shape.Optional, attribute) >= 0
                || Array.IndexOf(Standard, attribute) >= 0
                || (attribute == "visibility" && shape.Visible);
        }

        /// <summary>Whether XSLT 3.0 is the version that put an attribute on an element.</summary>
        /// <remarks>
        /// The first two are here rather than in every element's own list because they are standard
        /// attributes, so they are on all of them at once.
        /// </remarks>
        public static bool AddedIn30(XsltElement shape, string attribute)
        {
            return AddedIn30(attribute)
                || Array.IndexOf(shape.Since30, attribute) >= 0
                || (attribute == "visibility" && shape.Visible);
        }

        /// <summary>Whether a <em>standard</em> attribute is one XSLT 3.0 added.</summary>
        /// <remarks>
        /// Separate because a literal result element carries the standard attributes and has no shape to
        /// look one up in.
        /// </remarks>
        public static bool AddedIn30(string attribute)
        {
            return attribute is "expand-text" or "default-mode";
        }
    }
}
