using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// Parses the pattern syntax used by <c>xsl:template match</c>.
    /// </summary>
    /// <remarks>
    /// Patterns are a restricted subset of XPath: a sequence of steps joined by <c>/</c> or <c>//</c>, each
    /// step selecting on the child or attribute axis only, with optional predicates. The tokens come from the
    /// shared XPath scanner, and predicates are handed back to the full expression parser, so a pattern's
    /// predicate may contain arbitrary XPath.
    /// </remarks>
    internal static class PatternParser
    {
        /// <summary>Parses a pattern into its <c>|</c>-separated alternatives.</summary>
        /// <param name="source">The pattern text.</param>
        /// <param name="context">The static context supplying namespace bindings.</param>
        /// <returns>One compiled pattern per alternative.</returns>
        /// <exception cref="XsltException">The pattern is not valid.</exception>
        public static Pattern[] Parse(string source, IXPathStaticContext context)
        {
            try
            {
                return ParseAlternatives(source, context);
            }
            catch (XsltException exception) when (exception.Code is null or "XPST0003")
            {
                // A pattern is a smaller language than an expression, and the part of it that is an
                // expression is read by the expression parser — so a step it cannot read comes back as a
                // syntax error in XPath. Written in a match attribute it is not that: it is a pattern that
                // does not fit the pattern grammar, which XSLT names separately.
                throw XsltErrors.Error(XsltErrorCode.XTSE0340, exception.Message);
            }
        }

        private static Pattern[] ParseAlternatives(string source, IXPathStaticContext context)
        {
            List<XPathToken> tokens = XPathScanner.Scan(source);
            RequireNoGroupingFunction(tokens);

            // What a pattern may say is the vocabulary of the version this processor implements, so a run
            // claiming 2.0 reads the 2.0 grammar however new the stylesheet says it is. The suite is explicit
            // about it: half a dozen tests marked XSLT20 exactly are there to check that a 2.0 processor
            // refuses '$v//baz' and 'doc(…)' and the rest with XTSE0340.
            bool wide = context.SyntaxVersion.CompareTo(XsltVersion.V30) >= 0;

            // The outer parentheses of a whole pattern say nothing: XSLT 3.0 strips them and reads what is
            // inside by the same rules, so '(doc|cod)' is the two rules 'doc' and 'cod' with the priority
            // each has on its own, and not one rule of 0.5. A '.' inside is left to be refused below, the
            // predicate pattern not being a thing the parentheses may hold.
            if (wide
                && tokens[0].Kind == XPathTokenKind.LeftParen
                && tokens[1].Kind != XPathTokenKind.Dot
                && ClosesWholePattern(tokens))
            {
                int open = tokens[0].Position;
                int close = tokens[tokens.Count - 2].Position;
                return ParseAlternatives(source[(open + 1)..close], context);
            }

            if (wide && NeedsSelecting(source, tokens))
            {
                return new[] { ParseSelecting(source, tokens, context) };
            }

            int index = 0;
            List<Pattern> alternatives = new List<Pattern>();

            // The XSLT 3.0 predicate pattern, and the grammar puts it beside the union rather than inside
            // it: a pattern is either a union of node patterns or one '.' with predicates, never a mixture.
            // So it is read here rather than as an alternative, and anything after it is a syntax error —
            // which is what 'element(foo) | .[...]' is, however reasonable it looks.
            if (tokens[index].Kind == XPathTokenKind.Dot)
            {
                index++;

                // The predicate list may be empty, and the grammar means it: a bare '.' matches every item
                // there is. Which is what the specification's own worked implementation of fn:snapshot writes
                // to copy anything at all, so refusing it as a stylesheet saying nothing would be wrong.
                Pattern only = Pattern.PredicatePattern(
                    tokens[index].Kind == XPathTokenKind.LeftBracket
                        ? ParsePredicates(tokens, ref index, source, context)
                        : Array.Empty<Expr>());

                if (tokens[index].Kind != XPathTokenKind.End)
                {
                    throw Error(
                        source,
                        tokens[index],
                        "A '.' pattern is the whole pattern: it cannot be one branch of a union, because it "
                        + "matches items and the other branches match nodes.");
                }

                return new[] { only };
            }

            while (true)
            {
                alternatives.Add(ParseAlternative(tokens, ref index, source, context, wide));

                // The word 'union' is an XPath 2.0 operator, and a 2.0 pattern still may not use it: the
                // pattern grammar there writes the union as '|' and only as '|'.
                if (tokens[index].Kind == XPathTokenKind.Pipe
                    || (wide && tokens[index].Kind == XPathTokenKind.Union))
                {
                    index++;
                    continue;
                }

                if (tokens[index].Kind != XPathTokenKind.End)
                {
                    throw Error(source, tokens[index], "Unexpected token in pattern.");
                }

                return alternatives.ToArray();
            }
        }

        /// <summary>Whether the parenthesis the pattern opens with closes just before its end.</summary>
        private static bool ClosesWholePattern(List<XPathToken> tokens)
        {
            int depth = 0;

            for (int i = 0; i < tokens.Count - 1; i++)
            {
                switch (tokens[i].Kind)
                {
                    case XPathTokenKind.LeftParen:
                    case XPathTokenKind.LeftBracket:
                        depth++;
                        break;

                    case XPathTokenKind.RightParen:
                    case XPathTokenKind.RightBracket:
                        depth--;

                        if (depth == 0)
                        {
                            return i == tokens.Count - 2;
                        }

                        break;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a pattern has a shape that cannot be answered by walking upwards from the node.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the delta between the grammar the fast path reads and the one XSLT 3.0 defines, written
        /// out rather than guessed at. Three things are in it: a parenthesised expression standing where a
        /// step would (<c>(foo|baz)[*]</c>), a pattern rooted on a variable (<c>$v/baz</c>), and
        /// <c>intersect</c> or <c>except</c> between two paths. <c>doc()</c> and <c>root()</c> come in with
        /// them, being the other two functions the grammar lets a pattern hang from.
        /// </para>
        /// <para>
        /// Everything else stays on the fast path, and everything outside <em>both</em> grammars stays an
        /// error: <c>ancestor::doc</c> matches nothing here, and reaching for the expression parser whenever
        /// the strict one failed would have made it match something instead.
        /// </para>
        /// </remarks>
        /// <param name="source">The pattern text, for an error to quote.</param>
        /// <param name="tokens">The scanned pattern.</param>
        private static bool NeedsSelecting(string source, List<XPathToken> tokens)
        {
            int depth = 0;

            for (int i = 0; i < tokens.Count; i++)
            {
                XPathToken token = tokens[i];

                switch (token.Kind)
                {
                    case XPathTokenKind.RightParen:
                    case XPathTokenKind.RightBracket:
                        depth--;
                        continue;

                    case XPathTokenKind.LeftBracket:
                        depth++;
                        continue;

                    case XPathTokenKind.LeftParen:
                        // A parenthesis after a name is that function's argument list, which the fast path
                        // already reads for key(); anywhere else it is an expression standing as a step.
                        bool call = i > 0 && tokens[i - 1].Kind == XPathTokenKind.Name;
                        depth++;

                        if (depth != 1 || call)
                        {
                            continue;
                        }

                        // What may stand in those parentheses is a path, and '.' is not one. The predicate
                        // pattern sits beside the union in the grammar rather than inside it, so '.[…]' is
                        // the whole pattern or it is nothing — and it reads as a perfectly good expression,
                        // which is exactly why it has to be refused here rather than left to the parser.
                        if (i + 1 < tokens.Count && tokens[i + 1].Kind == XPathTokenKind.Dot)
                        {
                            throw Error(
                                source,
                                tokens[i + 1],
                                "A '.' pattern cannot be bracketed. It matches items rather than nodes, so "
                                + "it is the whole pattern and cannot stand where a path is expected.");
                        }

                        return true;
                }

                if (depth != 0)
                {
                    continue;
                }

                // The scanner has already settled that 'intersect' and 'except' are operators here rather
                // than the names of elements, which they are free to be anywhere else.
                if (token.Kind is XPathTokenKind.Variable
                    or XPathTokenKind.Intersect
                    or XPathTokenKind.Except)
                {
                    return true;
                }

                if (token.Kind == XPathTokenKind.Name
                    && token.Text is "doc" or "root"
                    && token.Prefix.Length == 0
                    && i + 1 < tokens.Count
                    && tokens[i + 1].Kind == XPathTokenKind.LeftParen)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads a pattern as an ordinary expression, to be matched by selecting rather than by walking up.
        /// </summary>
        /// <remarks>
        /// The pattern text is already a valid expression in every one of these shapes, so it is handed to
        /// the expression parser as it stands. What has to be added is the anchor. XSLT says a node matches
        /// where the pattern selects it from <em>some</em> node, and a pattern that does not begin at the
        /// root has to be tried from every one — so it is prefixed with
        /// <c>descendant-or-self::node()/</c>, which is exactly that set, and the parentheses keep a
        /// predicate written over the whole pattern bound to the whole of it.
        /// </remarks>
        /// <param name="source">The pattern text.</param>
        /// <param name="tokens">The scanned pattern, for deciding whether it begins at the root.</param>
        /// <param name="context">The static context supplying namespace bindings.</param>
        private static Pattern ParseSelecting(
            string source,
            List<XPathToken> tokens,
            IXPathStaticContext context)
        {
            bool rooted = tokens[0].Kind is XPathTokenKind.Slash
                or XPathTokenKind.DoubleSlash
                or XPathTokenKind.Variable
                || (tokens[0].Kind == XPathTokenKind.Name
                    && tokens[1].Kind == XPathTokenKind.LeftParen);

            if (rooted)
            {
                return Pattern.CreateSelection(XPathParser.Parse(source, context));
            }

            // The specification's adjustment of the first step: a child step is child-or-top, which from a
            // parentless element selects the element itself. Evaluated from root(N), that top is the
            // context node, so the same path with its first step on the self axis is what child-or-top
            // adds — and it is added as a second branch rather than folded in, because from every other
            // anchor the step means what it says.
            string selecting = "descendant-or-self::node()/(" + source + ")";
            string? fromTop = FirstStepOnSelf(source, tokens);

            return Pattern.CreateSelection(XPathParser.Parse(
                fromTop is null
                    ? selecting
                    : "(" + selecting + " | self::node()[not(parent::node())]/(" + fromTop + "))",
                context));
        }

        /// <summary>
        /// The pattern with its first step moved to the self axis, or null where the first step is not on
        /// the child axis and so has no top to be adjusted for.
        /// </summary>
        /// <param name="source">The pattern text.</param>
        /// <param name="tokens">The scanned pattern.</param>
        private static string? FirstStepOnSelf(string source, List<XPathToken> tokens)
        {
            XPathToken first = tokens[0];

            // Written out as child::, which is the one axis the adjustment applies to.
            if (first.Kind == XPathTokenKind.Name
                && first.Prefix.Length == 0
                && first.Text == "child"
                && tokens[1].Kind == XPathTokenKind.DoubleColon)
            {
                return "self::" + source[tokens[2].Position..];
            }

            // Or left implicit: a name, a wildcard or a kind test standing first, with no axis before it.
            // A name followed by '(' that is not a kind test is a function the pattern hangs from, and
            // those have been settled as rooted above.
            bool implicitChild = first.Kind == XPathTokenKind.Star
                || (first.Kind == XPathTokenKind.Name && tokens[1].Kind != XPathTokenKind.DoubleColon);

            return implicitChild ? "self::" + source[first.Position..] : null;
        }

        /// <summary>
        /// Refuses a pattern that asks about the group or the merge being processed.
        /// </summary>
        /// <remarks>
        /// A pattern is matched against a node to decide which rule reaches it, and that question is asked in
        /// places where no grouping and no merge is under way at all — so <c>current-group()</c> in one has
        /// no answer, and a stylesheet writing it has confused the pattern with the body that follows. The
        /// specification gives each of the four functions a code of its own, which is why they are named
        /// separately here rather than refused together.
        /// </remarks>
        private static void RequireNoGroupingFunction(List<XPathToken> tokens)
        {
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (tokens[i].Kind != XPathTokenKind.Name
                    || tokens[i + 1].Kind != XPathTokenKind.LeftParen)
                {
                    continue;
                }

                XsltErrorCode? code = tokens[i].Text switch
                {
                    "current-group" => XsltErrorCode.XTSE1060,
                    "current-grouping-key" => XsltErrorCode.XTSE1070,
                    "current-merge-group" => XsltErrorCode.XTSE3470,
                    "current-merge-key" => XsltErrorCode.XTSE3500,
                    _ => null,
                };

                if (code is not null)
                {
                    throw XsltErrors.Error(
                        code.Value,
                        $"'{tokens[i].Text}()' cannot be written in a pattern. A pattern decides whether a "
                        + "node matches, and that is asked in places where nothing of the kind is under "
                        + "way.");
                }
            }
        }

        private static Pattern ParseAlternative(
            List<XPathToken> tokens,
            ref int index,
            string source,
            IXPathStaticContext context,
            bool wide)
        {
            bool anchoredAtRoot = false;
            bool rootAnchorAllowsAnyDepth = false;
            bool nextStepFollowsAnyAncestor = false;

            if (tokens[index].Kind == XPathTokenKind.Slash)
            {
                index++;
                anchoredAtRoot = true;

                if (!StartsStep(tokens[index]))
                {
                    // A pattern of just '/' matches the root node and nothing else.
                    return Pattern.RootPattern;
                }
            }
            else if (tokens[index].Kind == XPathTokenKind.DoubleSlash)
            {
                index++;
                anchoredAtRoot = true;
                rootAnchorAllowsAnyDepth = true;
            }

            // A pattern may be anchored on a key rather than on the root: key('k', v) selects a set of nodes
            // by value, and the steps after it are relative to those. It is the one place in the pattern
            // grammar where a function call appears, and the only two functions that may appear are these.
            Expr? anchor = null;
            bool keyAnchorAllowsAnyDepth = false;

            if (!anchoredAtRoot && StartsIdKeyPattern(tokens, index))
            {
                anchor = ParseIdKey(tokens, ref index, source, context);

                if (tokens[index].Kind == XPathTokenKind.Slash)
                {
                    index++;
                }
                else if (tokens[index].Kind == XPathTokenKind.DoubleSlash)
                {
                    index++;
                    keyAnchorAllowsAnyDepth = true;
                }
                else
                {
                    // key('k', v) on its own: the node matches when it is one of the nodes the key selects,
                    // with no step to satisfy first.
                    return Pattern.CreateKeyAnchored(new List<PatternStep>(), anchor, false);
                }
            }

            List<PatternStep> steps = new List<PatternStep>();

            while (true)
            {
                steps.Add(ParseStep(tokens, ref index, source, context, nextStepFollowsAnyAncestor, wide));

                if (tokens[index].Kind == XPathTokenKind.Slash)
                {
                    index++;
                    nextStepFollowsAnyAncestor = false;
                }
                else if (tokens[index].Kind == XPathTokenKind.DoubleSlash)
                {
                    index++;
                    nextStepFollowsAnyAncestor = true;
                }
                else
                {
                    break;
                }
            }

            // Each step already carries the connector written immediately before it, which is exactly the flag
            // matching needs: when a step matches, that flag says whether the step to its left must be the
            // immediate parent or merely some ancestor.
            return anchor is null
                ? Pattern.Create(steps, anchoredAtRoot, rootAnchorAllowsAnyDepth)
                : Pattern.CreateKeyAnchored(steps, anchor, keyAnchorAllowsAnyDepth);
        }

        private static PatternStep ParseStep(
            List<XPathToken> tokens,
            ref int index,
            string source,
            IXPathStaticContext context,
            bool connectsToAnyAncestor,
            bool wide)
        {
            Axis axis = Axis.Child;
            bool explicitAxis = false;

            if (tokens[index].Kind == XPathTokenKind.At)
            {
                index++;
                axis = Axis.Attribute;
            }
            else if (tokens[index].Kind == XPathTokenKind.Name
                && tokens[index + 1].Kind == XPathTokenKind.DoubleColon)
            {
                explicitAxis = true;
                string axisName = tokens[index].Text;
                axis = axisName switch
                {
                    "child" => Axis.Child,
                    "attribute" => Axis.Attribute,
                    "self" when wide => Axis.Self,
                    "descendant" when wide => Axis.Descendant,
                    "descendant-or-self" when wide => Axis.DescendantOrSelf,
                    "namespace" when wide => Axis.Namespace,
                    _ when wide => throw Error(source, tokens[index],
                        $"'{axisName}' is not an axis a pattern may use. A pattern is matched upwards from "
                        + "the node, so only the axes that reach downwards from an ancestor are open to it: "
                        + "child, attribute, namespace, self, descendant and descendant-or-self."),
                    _ => throw Error(source, tokens[index],
                        $"Only the child and attribute axes may be used in a pattern, but '{axisName}' was "
                        + "written. The other three a pattern can afford were added in XSLT 3.0, and this "
                        + "processor is reading the 2.0 grammar."),
                };

                index += 2;
            }

            // The same production a path step uses, read by the same code: a pattern's node test is not a
            // smaller language than an expression's, and keeping a second copy here is what made it one.
            NodeTest test = XPathParser.ParseNodeTestNested(tokens, ref index, source, context, axis);
            Expr[] predicates = ParsePredicates(tokens, ref index, source, context);

            // attribute() with no axis written stands on the attribute axis and namespace-node() on the
            // namespace axis, as XPath's abbreviated syntax has it: the test names the kind, and the kind is
            // on one axis only.
            if (!explicitAxis && axis == Axis.Child
                && test is KindNodeTest { Kind: NodeKind.Attribute or NodeKind.Namespace } kindTest)
            {
                axis = kindTest.Kind == NodeKind.Attribute ? Axis.Attribute : Axis.Namespace;
            }

            return new PatternStep(axis, test, predicates, ReachOf(axis, connectsToAnyAncestor), explicitAxis);
        }

        /// <summary>
        /// Works out where the step to the left of this one has to hold.
        /// </summary>
        /// <remarks>
        /// The connector and the axis say the same sort of thing and are combined rather than kept apart.
        /// <c>a/descendant::b</c> is <c>a//b</c>, and <c>a//self::b</c> asks for a <c>b</c> that is at or
        /// under an <c>a</c> — so a <c>//</c> written before a step widens whatever the axis had already
        /// reached, and widening something already wide changes nothing.
        /// </remarks>
        /// <param name="axis">The axis the step was written on.</param>
        /// <param name="followsAnyAncestor">Whether the step was joined to the one before it by <c>//</c>.</param>
        private static PatternReach ReachOf(Axis axis, bool followsAnyAncestor)
        {
            PatternReach reach = axis switch
            {
                Axis.Self => PatternReach.Self,
                Axis.Descendant => PatternReach.AnyAncestor,
                Axis.DescendantOrSelf => PatternReach.AnyAncestorOrSelf,
                _ => PatternReach.Parent,
            };

            if (!followsAnyAncestor)
            {
                return reach;
            }

            return reach switch
            {
                PatternReach.Parent => PatternReach.AnyAncestor,
                PatternReach.Self => PatternReach.AnyAncestorOrSelf,
                _ => reach,
            };
        }

        private static Expr[] ParsePredicates(
            List<XPathToken> tokens,
            ref int index,
            string source,
            IXPathStaticContext context)
        {
            if (tokens[index].Kind != XPathTokenKind.LeftBracket)
            {
                return Array.Empty<Expr>();
            }

            List<Expr> predicates = new List<Expr>();
            while (tokens[index].Kind == XPathTokenKind.LeftBracket)
            {
                index++;
                predicates.Add(XPathParser.ParseNested(tokens, ref index, source, context));

                if (tokens[index].Kind != XPathTokenKind.RightBracket)
                {
                    throw Error(source, tokens[index], "Expected ']'.");
                }

                index++;
            }

            return predicates.ToArray();
        }

        /// <summary>Whether what follows is <c>key(…)</c> or <c>id(…)</c> rather than a step.</summary>
        /// <remarks>
        /// An element may be called <c>key</c>, so the parenthesis is what settles it: <c>match="key"</c> is
        /// a name test and <c>match="key('k', 1)"</c> is an anchor. Same rule that already lets an element be
        /// called <c>div</c>.
        /// </remarks>
        private static bool StartsIdKeyPattern(List<XPathToken> tokens, int index)
        {
            return tokens[index].Kind == XPathTokenKind.Name
                && tokens[index].Prefix.Length == 0
                && tokens[index].Text is "key" or "id"
                && tokens[index + 1].Kind == XPathTokenKind.LeftParen;
        }

        private static Expr ParseIdKey(
            List<XPathToken> tokens,
            ref int index,
            string source,
            IXPathStaticContext context)
        {
            XPathToken opening = tokens[index];

            if (opening.Text == "id")
            {
                // id(v) selects the elements an ID names — an xml:id, or an attribute the document's type
                // declaration typed ID — and the steps after it are relative to those, as with a key. The
                // value is held to the same grammar as a key's: a literal or a variable, not an expression.
                index += 2;
                Expr ids = ParseKeyValue(tokens, ref index, source, context);
                Expr? document = null;

                // XSLT 3.0 lets the pattern name the document searched, as a variable holding it.
                if (tokens[index].Kind == XPathTokenKind.Comma)
                {
                    if (context.SyntaxVersion.CompareTo(XsltVersion.V30) < 0)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0340,
                            "id() in a pattern takes one argument under XSLT 2.0; naming the document searched "
                            + "is XSLT 3.0.");
                    }

                    index++;
                    document = ParseKeyValue(tokens, ref index, source, context);
                }

                if (tokens[index].Kind != XPathTokenKind.RightParen)
                {
                    throw Error(source, tokens[index], "Expected ')'.");
                }

                index++;
                return new IdExpr("id", ids, document);
            }

            index += 2;

            // The grammar here is narrower than an argument list's, and deliberately so: a pattern says which
            // nodes it describes, and it may not do so in terms of where the transformation has got to. The
            // name must be written out, and the value must be a literal or a variable — not an expression
            // that could read the context, which is why key('k', 40+2) is refused rather than computed.
            if (tokens[index].Kind != XPathTokenKind.StringLiteral)
            {
                throw Error(source, tokens[index], "The key named in a pattern must be written as a string.");
            }

            Expr name = new StringLiteralExpr(tokens[index].Text);
            index++;

            if (tokens[index].Kind != XPathTokenKind.Comma)
            {
                throw Error(source, tokens[index], "key() in a pattern takes a name and a value.");
            }

            index++;
            Expr value = ParseKeyValue(tokens, ref index, source, context);

            if (tokens[index].Kind != XPathTokenKind.RightParen)
            {
                throw Error(source, tokens[index], "Expected ')'.");
            }

            index++;

            // The name is resolved when the pattern is matched rather than now: a pattern is parsed against a
            // static context, which knows the namespaces but not the stylesheet's keys.
            return new KeyExpr(-1, name, value) { Package = (context as StylesheetCompiler)?.CurrentPackage ?? 0 };
        }

        /// <summary>Reads the value a key pattern looks up: a literal, or a variable reference.</summary>
        private static Expr ParseKeyValue(
            List<XPathToken> tokens,
            ref int index,
            string source,
            IXPathStaticContext context)
        {
            XPathToken token = tokens[index];

            if (token.Kind is XPathTokenKind.StringLiteral or XPathTokenKind.Number or XPathTokenKind.Variable)
            {
                int start = index;
                Expr value = XPathParser.ParseArgumentNested(tokens, ref index, source, context);

                // A literal and a variable reference are each one token, so anything that read more than one
                // was an expression built on top of one — key('k', 40+2) starts with a number and is not a
                // number. Counting the tokens says so without a second grammar to keep in step.
                if (index == start + 1)
                {
                    return value;
                }

                index = start;
            }

            throw Error(
                source,
                token,
                "The value a key pattern looks up must be a literal or a variable reference.");
        }

        private static bool StartsStep(XPathToken token)
        {
            return token.Kind is XPathTokenKind.At or XPathTokenKind.Star or XPathTokenKind.Name;
        }

        private static XsltException Error(string source, XPathToken token, string message)
        {
            return new XsltException($"{message} (at offset {token.Position} in pattern \"{source}\")");
        }
    }
}
