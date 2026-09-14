using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A recursive-descent parser for XPath 1.0, producing the expression tree that both backends execute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grammar is transcribed directly from the specification, one method per production, so operator
    /// precedence falls out of the nesting: <c>or</c> binds loosest, then <c>and</c>, equality, relational,
    /// additive, multiplicative, unary minus, and finally <c>|</c>.
    /// </para>
    /// <para>
    /// One rule is easy to get wrong and matters a great deal in namespaced documents: in XSLT 1.0 an
    /// <em>unprefixed</em> name in an XPath expression is in <em>no namespace</em>, even when a default
    /// namespace is in scope. <c>match="Document"</c> therefore does not match an element in the ISO 20022
    /// namespace; a prefix must be declared and used. Only prefixed names are passed to
    /// <see cref="IXPathStaticContext.ResolvePrefix"/>.
    /// </para>
    /// </remarks>
    public sealed class XPathParser
    {
        private static readonly HashSet<string> s_nodeTypes = new(StringComparer.Ordinal)
        {
            "node", "text", "comment", "processing-instruction", "namespace-node",
        };

        /// <summary>
        /// The kind tests XPath 2.0 adds to the four XPath 1.0 had.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="s_nodeTypes"/> because those take no argument and these do: an
        /// <c>element</c> test may name the element it wants, and a <c>document-node</c> test may say what
        /// its element child must be.
        /// </remarks>
        private static readonly HashSet<string> s_kindTests = new(StringComparer.Ordinal)
        {
            "element", "attribute", "document-node", "schema-element", "schema-attribute",
        };

        /// <summary>
        /// The names XPath 2.0 keeps for itself, which therefore name no function.
        /// </summary>
        /// <remarks>
        /// Each is a kind test or a keyword the grammar needs to read unambiguously, so <c>node(1)</c> is
        /// not an unknown function but a sentence the grammar has no reading for — <c>XPST0003</c> rather
        /// than <c>XPST0017</c>. A prefix lifts the reservation, <c>my:node()</c> being an ordinary name.
        /// </remarks>
        private static readonly HashSet<string> s_reservedFunctionNames = new(StringComparer.Ordinal)
        {
            "attribute", "comment", "document-node", "element", "empty-sequence", "if", "item",
            "namespace-node", "node", "processing-instruction", "schema-attribute", "schema-element", "text",
            "typeswitch",
        };

        private readonly List<XPathToken> m_tokens;
        private readonly IXPathStaticContext m_context;
        private readonly string m_source;
        private int m_index;
        private ComparisonContext? m_comparing;

        /// <summary>
        /// What the comparisons in this expression are written among: the collation and the namespaces.
        /// </summary>
        /// <remarks>
        /// One per expression rather than one per comparison. Every comparison in an expression stands at
        /// the same element, so the answer is the same for all of them, and working it out means walking up
        /// the tree for the collation and building the namespace bindings — worth doing once, and only for
        /// an expression that has a comparison in it at all.
        /// </remarks>
        private ComparisonContext Comparing => m_comparing ??= ComparisonContext.For(m_context);

        private XPathParser(string source, IXPathStaticContext context)
        {
            m_source = source;
            m_context = context;
            m_tokens = XPathScanner.Scan(source);
        }

        private XPathParser(List<XPathToken> tokens, string source, IXPathStaticContext context, int index)
        {
            m_source = source;
            m_context = context;
            m_tokens = tokens;
            m_index = index;
        }

        /// <summary>
        /// Parses one expression out of an already-scanned token list, advancing the caller's position.
        /// </summary>
        /// <remarks>
        /// Used by the pattern parser, which shares a token list with the expressions embedded in its
        /// predicates rather than re-scanning slices of the source text.
        /// </remarks>
        /// <param name="tokens">The shared token list.</param>
        /// <param name="index">On entry the first token of the expression; on return the token after it.</param>
        /// <param name="source">The original text, used for error messages.</param>
        /// <param name="context">The static context.</param>
        /// <returns>The compiled expression.</returns>
        internal static Expr ParseNested(
            List<XPathToken> tokens,
            ref int index,
            string source,
            IXPathStaticContext context)
        {
            XPathParser parser = new XPathParser(tokens, source, context, index);
            Expr result = parser.ParseExpression();
            index = parser.m_index;
            return result;
        }

        /// <summary>
        /// Reads one argument out of a token stream, stopping at the comma that separates it from the next.
        /// </summary>
        /// <remarks>
        /// The comma is an operator at the top level of an expression and a separator inside an argument
        /// list, which is why this exists beside <see cref="ParseNested"/>: reading <c>'k', $v</c> as an
        /// expression makes it one sequence-valued argument and leaves nothing where the comma should be.
        /// </remarks>
        internal static Expr ParseArgumentNested(
            List<XPathToken> tokens,
            ref int index,
            string source,
            IXPathStaticContext context)
        {
            XPathParser parser = new XPathParser(tokens, source, context, index);
            Expr result = parser.ParseExprSingle();
            index = parser.m_index;
            return result;
        }

        /// <summary>
        /// Reads one node test out of a token stream, which is what a pattern step needs.
        /// </summary>
        /// <remarks>
        /// A pattern's node test is the same production as a path's, so it is read by the same code. The
        /// pattern parser had a shortened copy of this and the copy was short of the kind tests — a pattern
        /// could not say <c>element()</c> or <c>document-node()</c> or <c>*:local</c>, all of which a path
        /// two lines away could say perfectly well.
        /// </remarks>
        /// <param name="tokens">The scanned pattern.</param>
        /// <param name="index">Where the node test starts; left after it.</param>
        /// <param name="source">The pattern text, for error messages.</param>
        /// <param name="context">The static context supplying namespace bindings.</param>
        /// <param name="axis">The axis the step walks, which decides what an unqualified test means.</param>
        internal static NodeTest ParseNodeTestNested(
            List<XPathToken> tokens,
            ref int index,
            string source,
            IXPathStaticContext context,
            Axis axis)
        {
            XPathParser parser = new XPathParser(tokens, source, context, index);
            NodeTest result = parser.ParseNodeTest(axis);
            index = parser.m_index;
            return result;
        }

        /// <summary>
        /// Parses an XPath expression.
        /// </summary>
        /// <param name="expression">The expression text.</param>
        /// <param name="context">The static context supplying namespace bindings and variable slots.</param>
        /// <returns>The compiled expression.</returns>
        /// <exception cref="XsltException">The expression is not valid XPath, or refers to something unbound.</exception>
        public static Expr Parse(string expression, IXPathStaticContext context)
        {
            XPathParser parser = new XPathParser(expression, context);
            Expr result = parser.ParseExpression();
            parser.Expect(XPathTokenKind.End);
            return result;
        }

        /// <summary>
        /// Parses a sequence type on its own, which is what an <c>as</c> attribute holds.
        /// </summary>
        /// <param name="type">The type as written.</param>
        /// <param name="context">The static context supplying namespace bindings.</param>
        /// <returns>The parsed type.</returns>
        /// <exception cref="XsltException">The text is not a sequence type.</exception>
        public static XdmSequenceType ParseType(string type, IXPathStaticContext context)
        {
            XPathParser parser = new XPathParser(type, context);
            XdmSequenceType result = parser.ParseSequenceType();
            parser.Expect(XPathTokenKind.End);
            return result;
        }

        private XPathToken Current => m_tokens[m_index];

        private XPathToken Peek(int offset)
        {
            int index = m_index + offset;
            return index < m_tokens.Count ? m_tokens[index] : m_tokens[^1];
        }

        /// <summary>
        /// Parses a full expression, which in XPath 2.0 may be several joined by commas into a sequence.
        /// </summary>
        /// <remarks>
        /// The comma is an operator here and a separator inside a function's argument list, so the two must
        /// be parsed at different levels: arguments and the operands of every other operator use
        /// <see cref="ParseExprSingle"/>, which stops at a comma and leaves it to its caller.
        /// </remarks>
        private Expr ParseExpression()
        {
            Expr first = ParseExprSingle();

            if (Current.Kind != XPathTokenKind.Comma || m_context.LegacySyntax)
            {
                return first;
            }

            List<Expr> items = new List<Expr> { first };
            while (Current.Kind == XPathTokenKind.Comma)
            {
                m_index++;
                items.Add(ParseExprSingle());
            }

            return new SequenceExpr(items.ToArray());
        }

        /// <summary>
        /// Range variables in scope, innermost last. A variable's slot is its position here, so bindings at
        /// the same depth in different branches share one — which the binding expressions allow for by
        /// saving and restoring what was there.
        /// </summary>
        private readonly List<(string Uri, string Local)> m_rangeScope = new();

        /// <summary>
        /// The deepest the range scope ever got, which is how many slots an evaluation of this expression can
        /// need.
        /// </summary>
        /// <remarks>
        /// An inline function's closure copies the range-variable store, so it has to be sized once and not
        /// grown: growing replaces the array, and the copy the closure is holding would stop being the one
        /// its body reads.
        /// </remarks>
        private int m_rangeHighWater;

        /// <summary>
        /// Refuses to go on where the stack is nearly spent, so that a deeply nested expression is an error
        /// rather than a stack overflow.
        /// </summary>
        private static void RefuseDeepNesting()
        {
            if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0003,
                    "The expression is nested too deeply to read. Every level of nesting costs stack to "
                    + "parse, and this one has more levels than there is stack for.");
            }
        }

        /// <summary>
        /// Parses one expression, which in XPath 2.0 may be a binding or a conditional before it is anything
        /// else.
        /// </summary>
        private Expr ParseExprSingle()
        {
            // Recursive descent costs a frame per precedence level per nesting, and there are two dozen
            // levels — so an expression nested a few hundred deep runs the stack out, and a stack overflow
            // is not an error a caller can catch: it takes the process down. A guard here turns it into an
            // ordinary refusal, and this is where it belongs because every nesting of an expression passes
            // through. A type nests on its own, without an expression between the levels, so it has the
            // same guard of its own.
            RefuseDeepNesting();

            if (!m_context.LegacySyntax && Current.Kind == XPathTokenKind.Name
                && Current.Prefix.Length == 0)
            {
                switch (Current.Text)
                {
                    // A keyword only where one can start an expression and the shape that follows fits: an
                    // element may be named "for", and "if(...)" would otherwise read as a function call.
                    case "for" when Peek(1).Kind == XPathTokenKind.Variable:
                        return ParseFor();

                    case "let" when IsXPath30 && Peek(1).Kind == XPathTokenKind.Variable:
                        return ParseLet();

                    case "some" when Peek(1).Kind == XPathTokenKind.Variable:
                        return ParseQuantified(requireAll: false);

                    case "every" when Peek(1).Kind == XPathTokenKind.Variable:
                        return ParseQuantified(requireAll: true);

                    case "if" when Peek(1).Kind == XPathTokenKind.LeftParen:
                        return ParseIf();
                }
            }

            return ParseOr();
        }

        /// <summary>Parses <c>for $x in E, $y in E return E</c>, which nests one binding per clause.</summary>
        private Expr ParseFor()
        {
            m_index++;
            int depth = m_rangeScope.Count;

            List<(int Slot, Expr Sequence)> bindings = ParseBindings();
            ExpectKeyword("return");

            Expr body = ParseExprSingle();
            m_rangeScope.RemoveRange(depth, m_rangeScope.Count - depth);

            // Written right to left, so that "for $x in A, $y in B return E" is "for $x in A return
            // (for $y in B return E)" — which is what makes the clauses nest rather than run in parallel.
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                body = new ForExpr(bindings[i].Slot, bindings[i].Sequence, body);
            }

            return body;
        }

        /// <summary>
        /// Parses <c>let $x := E, $y := F return G</c>, which XPath 3.0 adds and 2.0 has no equivalent of.
        /// </summary>
        /// <remarks>
        /// Each binding is in scope for those after it as well as for the body, so <c>let $a := 1, $b := $a
        /// + 1 return $b</c> works — which is why the clauses nest right to left, exactly as <c>for</c>'s do.
        /// </remarks>
        private Expr ParseLet()
        {
            m_index++;
            int depth = m_rangeScope.Count;

            List<(int Slot, Expr Value)> bindings = new List<(int, Expr)>();

            while (true)
            {
                XPathToken variable = Current;
                if (variable.Kind != XPathTokenKind.Variable)
                {
                    throw Error(variable, "A variable was expected after 'let'.");
                }

                m_index++;
                Expect(XPathTokenKind.Assign);

                Expr value = ParseExprSingle();

                string uri = variable.Prefix.Length == 0 ? string.Empty : ResolvePrefixOrThrow(variable);
                bindings.Add((m_rangeScope.Count, value));
                m_rangeScope.Add((uri, variable.Text));
                m_rangeHighWater = Math.Max(m_rangeHighWater, m_rangeScope.Count);

                if (Current.Kind != XPathTokenKind.Comma)
                {
                    break;
                }

                m_index++;
            }

            ExpectKeyword("return");

            Expr body = ParseExprSingle();
            m_rangeScope.RemoveRange(depth, m_rangeScope.Count - depth);

            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                body = new LetExpr(bindings[i].Slot, bindings[i].Value, body);
            }

            return body;
        }

        private Expr ParseQuantified(bool requireAll)
        {
            m_index++;
            int depth = m_rangeScope.Count;

            List<(int Slot, Expr Sequence)> bindings = ParseBindings();
            ExpectKeyword("satisfies");

            Expr body = ParseExprSingle();
            m_rangeScope.RemoveRange(depth, m_rangeScope.Count - depth);

            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                body = new QuantifiedExpr(bindings[i].Slot, bindings[i].Sequence, body, requireAll);
            }

            return body;
        }

        /// <summary>
        /// Parses the <c>$x in E</c> clauses shared by <c>for</c>, <c>some</c> and <c>every</c>.
        /// </summary>
        /// <remarks>
        /// Each variable comes into scope for the clauses after it as well as for the body, so
        /// <c>for $x in A, $y in $x/b return …</c> works. The sequence is therefore parsed before the
        /// variable is bound, and the binding added afterwards.
        /// </remarks>
        private List<(int Slot, Expr Sequence)> ParseBindings()
        {
            List<(int Slot, Expr Sequence)> bindings = new();

            while (true)
            {
                XPathToken variable = Current;
                if (variable.Kind != XPathTokenKind.Variable)
                {
                    throw Error(variable, "A variable was expected after 'for', 'some' or 'every'.");
                }

                m_index++;
                ExpectKeyword("in");

                Expr sequence = ParseExprSingle();

                string uri = variable.Prefix.Length == 0 ? string.Empty : ResolvePrefixOrThrow(variable);
                bindings.Add((m_rangeScope.Count, sequence));
                m_rangeScope.Add((uri, variable.Text));
                m_rangeHighWater = Math.Max(m_rangeHighWater, m_rangeScope.Count);

                if (Current.Kind != XPathTokenKind.Comma)
                {
                    return bindings;
                }

                m_index++;
            }
        }

        private Expr ParseIf()
        {
            m_index++;
            Expect(XPathTokenKind.LeftParen);
            Expr test = ParseExpression();
            Expect(XPathTokenKind.RightParen);

            ExpectKeyword("then");
            Expr whenTrue = ParseExprSingle();
            ExpectKeyword("else");

            // The else is not optional: an expression must have a value whichever way the test goes.
            return new IfExpr(test, whenTrue, ParseExprSingle());
        }

        private void ExpectKeyword(string keyword)
        {
            if (Current.Kind != XPathTokenKind.Name || Current.Prefix.Length != 0 || Current.Text != keyword)
            {
                throw Error(Current, $"'{keyword}' was expected.");
            }

            m_index++;
        }

        private Expr ParseOr()
        {
            Expr left = ParseAnd();
            while (Current.Kind == XPathTokenKind.Or)
            {
                m_index++;
                left = new BinaryExpr(BinaryOperator.Or, left, ParseAnd());
            }

            return left;
        }

        private Expr ParseAnd()
        {
            Expr left = ParseEquality();
            while (Current.Kind == XPathTokenKind.And)
            {
                m_index++;
                left = new BinaryExpr(BinaryOperator.And, left, ParseEquality());
            }

            return left;
        }

        private Expr ParseEquality()
        {
            Expr left = ParseValueComparison();
            while (true)
            {
                BinaryOperator op;
                switch (Current.Kind)
                {
                    case XPathTokenKind.Equal:
                        op = BinaryOperator.Equal;
                        break;

                    case XPathTokenKind.NotEqual:
                        op = BinaryOperator.NotEqual;
                        break;

                    default:
                        return left;
                }

                m_index++;
                left = new BinaryExpr(op, left, ParseValueComparison(), m_context.Version)
                {
                    Comparing = Comparing,
                };
            }
        }

        /// <summary>
        /// Parses the XPath 2.0 value comparisons, which sit alongside the general ones but compare a single
        /// value against a single value rather than asking whether any pair of items matches.
        /// </summary>
        private Expr ParseValueComparison()
        {
            Expr left = ParseRelational();

            // The node comparisons sit at the same level: they compare, and they do not chain either.
            if (Current.Kind is XPathTokenKind.Is or XPathTokenKind.Precedes or XPathTokenKind.Follows)
            {
                int kind = Current.Kind switch
                {
                    XPathTokenKind.Is => 0,
                    XPathTokenKind.Precedes => -1,
                    _ => 1,
                };

                m_index++;
                return new NodeComparisonExpr(left, ParseRelational(), kind);
            }

            if (Current.Kind != XPathTokenKind.ValueComparison)
            {
                return left;
            }

            string name = Current.Text;
            m_index++;

            BinaryOperator op = name switch
            {
                "eq" => BinaryOperator.Equal,
                "ne" => BinaryOperator.NotEqual,
                "lt" => BinaryOperator.LessThan,
                "le" => BinaryOperator.LessThanOrEqual,
                "gt" => BinaryOperator.GreaterThan,
                _ => BinaryOperator.GreaterThanOrEqual,
            };

            // Value comparisons do not chain: 'a eq b eq c' is a syntax error, not a nested comparison.
            return new ValueComparisonExpr(op, left, ParseRelational()) { Comparing = Comparing };
        }

        /// <summary>Parses the XPath 2.0 range operator, <c>1 to 5</c>.</summary>
        private Expr ParseRange()
        {
            Expr left = ParseAdditive();

            if (Current.Kind != XPathTokenKind.To)
            {
                return left;
            }

            m_index++;
            return new RangeExpr(left, ParseAdditive(), m_context.Version);
        }

        /// <summary>
        /// Parses the XPath 3.0 string concatenation operator, <c>'a' || 'b'</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It sits between comparison and range, which is what the two tests written for it pin down:
        /// <c>12 || 34 - 50</c> is <c>"12-16"</c>, so arithmetic binds tighter, and
        /// <c>"1234" eq 12 || 34</c> is true, so comparison binds looser. Anywhere else and one of them
        /// changes meaning.
        /// </para>
        /// <para>
        /// It is <c>fn:concat</c> written as an operator and nothing more, so it is compiled into one: the
        /// atomization, the empty sequence reading as the empty string, and the refusal of a sequence of two
        /// all follow from that rather than being restated here.
        /// </para>
        /// </remarks>
        private Expr ParseStringConcat()
        {
            Expr left = ParseRange();

            if (Current.Kind != XPathTokenKind.Concat || !IsXPath30)
            {
                return left;
            }

            List<Expr> parts = new List<Expr> { left };

            while (Current.Kind == XPathTokenKind.Concat)
            {
                m_index++;
                parts.Add(ParseRange());
            }

            // One call rather than a chain of two-argument ones, which is the same answer and one pass.
            return CreateLibraryFunction("concat", parts.ToArray());
        }

        private Expr ParseRelational()
        {
            Expr left = ParseStringConcat();
            while (true)
            {
                BinaryOperator op;
                switch (Current.Kind)
                {
                    case XPathTokenKind.LessThan:
                        op = BinaryOperator.LessThan;
                        break;

                    case XPathTokenKind.LessThanOrEqual:
                        op = BinaryOperator.LessThanOrEqual;
                        break;

                    case XPathTokenKind.GreaterThan:
                        op = BinaryOperator.GreaterThan;
                        break;

                    case XPathTokenKind.GreaterThanOrEqual:
                        op = BinaryOperator.GreaterThanOrEqual;
                        break;

                    default:
                        return left;
                }

                m_index++;
                left = new BinaryExpr(op, left, ParseStringConcat(), m_context.Version)
                {
                    Comparing = Comparing,
                };
            }
        }

        private Expr ParseAdditive()
        {
            Expr left = ParseMultiplicative();
            while (true)
            {
                BinaryOperator op;
                switch (Current.Kind)
                {
                    case XPathTokenKind.Plus:
                        op = BinaryOperator.Add;
                        break;

                    case XPathTokenKind.Minus:
                        op = BinaryOperator.Subtract;
                        break;

                    default:
                        return left;
                }

                m_index++;
                left = new BinaryExpr(op, left, ParseMultiplicative(), m_context.Version);
            }
        }

        private Expr ParseMultiplicative()
        {
            Expr left = ParseUnary();
            while (true)
            {
                BinaryOperator op;
                switch (Current.Kind)
                {
                    case XPathTokenKind.Multiply:
                        op = BinaryOperator.Multiply;
                        break;

                    case XPathTokenKind.Div:
                        op = BinaryOperator.Divide;
                        break;

                    case XPathTokenKind.IDiv:
                        op = BinaryOperator.IntegerDivide;
                        break;

                    case XPathTokenKind.Mod:
                        op = BinaryOperator.Modulo;
                        break;

                    default:
                        return left;
                }

                m_index++;
                left = new BinaryExpr(op, left, ParseUnary(), m_context.Version);
            }
        }

        /// <summary>
        /// Parses a leading sign where XPath 1.0 puts it: outside a union, and so outside everything below.
        /// </summary>
        /// <remarks>
        /// XPath 2.0 moves it. There <c>UnaryExpr</c> sits between <c>CastExpr</c> and <c>ValueExpr</c>, so
        /// the sign binds tighter than <c>cast as</c> and <c>instance of</c> rather than looser:
        /// <c>-129 castable as xs:byte</c> asks whether −129 is an <c>xs:byte</c>, where reading it the other
        /// way asks for the negative of a boolean. See <see cref="ParseSignedValue"/>.
        /// </remarks>
        private Expr ParseUnary()
        {
            if (!m_context.LegacySyntax || Current.Kind != XPathTokenKind.Minus)
            {
                return ParseUnion();
            }

            // A run of signs is counted rather than recursed over, because the run is not an expression
            // nesting and so never passes the guard in ParseExprSingle: a few thousand of them would run the
            // parser out of stack, and the tree they built would run the evaluator out of it next. Two
            // negations undo each other, and a 1.0 negation reads its operand as a number, so a pair is
            // number() and what a run amounts to is that pair or a single negation, by its parity.
            int signs = 0;
            while (Current.Kind == XPathTokenKind.Minus)
            {
                m_index++;
                signs++;
            }

            Expr negated = new NegateExpr(ParseUnion(), m_context.Version);
            return signs % 2 == 0 ? new NegateExpr(negated, m_context.Version) : negated;
        }

        private Expr ParseUnion()
        {
            Expr left = ParseIntersectExcept();
            while (Current.Kind is XPathTokenKind.Pipe or XPathTokenKind.Union)
            {
                m_index++;
                left = new UnionExpr(left, ParseIntersectExcept());
            }

            return left;
        }

        /// <summary>
        /// Parses <c>intersect</c> and <c>except</c>, which bind tighter than <c>|</c>.
        /// </summary>
        private Expr ParseIntersectExcept()
        {
            Expr left = ParseInstanceOf();

            while (Current.Kind is XPathTokenKind.Intersect or XPathTokenKind.Except)
            {
                bool keepShared = Current.Kind == XPathTokenKind.Intersect;
                m_index++;
                left = new NodeSetOperationExpr(left, ParseInstanceOf(), keepShared);
            }

            return left;
        }

        /// <summary>
        /// Parses the four type operators, which nest in the order the grammar gives them:
        /// <c>instance of</c> outermost, then <c>treat as</c>, <c>castable as</c> and <c>cast as</c>.
        /// </summary>
        private Expr ParseInstanceOf()
        {
            Expr value = ParseTreatAs();

            if (!TakeKeyword("instance"))
            {
                return value;
            }

            ExpectKeyword("of");
            return new InstanceOfExpr(value, ParseSequenceType());
        }

        private Expr ParseTreatAs()
        {
            Expr value = ParseCastable();

            if (!TakeKeyword("treat"))
            {
                return value;
            }

            ExpectKeyword("as");
            return new TreatAsExpr(value, ParseSequenceType());
        }

        private Expr ParseCastable()
        {
            Expr value = ParseCast();

            if (!TakeKeyword("castable"))
            {
                return value;
            }

            ExpectKeyword("as");
            (XdmType.BuiltInType? type, XdmSchemaType? schemaType, bool allowEmpty) = ParseSingleType();

            return type is XdmType.BuiltInType builtIn
                ? new CastExpr(value, builtIn, allowEmpty, testOnly: true, m_context.InScopeNamespaces)
                : new CastExpr(value, schemaType!, allowEmpty, testOnly: true, m_context.InScopeNamespaces);
        }

        private Expr ParseCast()
        {
            Expr value = ParseArrow();

            if (!TakeKeyword("cast"))
            {
                return value;
            }

            ExpectKeyword("as");
            (XdmType.BuiltInType? type, XdmSchemaType? schemaType, bool allowEmpty) = ParseSingleType();

            if (type is not XdmType.BuiltInType builtIn)
            {
                return new CastExpr(value, schemaType!, allowEmpty, testOnly: false, m_context.InScopeNamespaces);
            }

            XdmType.RequireLiteralNameBelowThree(builtIn, value, m_context.SyntaxVersion);

            return new CastExpr(value, builtIn, allowEmpty, testOnly: false, m_context.InScopeNamespaces);
        }

        /// <summary>
        /// Parses the arrow, <c>$x =&gt; f(1)</c>, which is another way of writing <c>f($x, 1)</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Worth having because it reverses the order a chain of calls is written in.
        /// <c>tokenize(normalize-space(upper-case($s)), ' ')</c> is read inside out; the same thing written
        /// <c>$s =&gt; upper-case() =&gt; normalize-space() =&gt; tokenize(' ')</c> is read in the order the
        /// steps happen.
        /// </para>
        /// <para>
        /// It binds tighter than <c>cast as</c> and looser than a leading sign, which is where the grammar
        /// puts it: <c>-$x =&gt; abs()</c> takes the absolute value of the negation.
        /// </para>
        /// </remarks>
        private Expr ParseArrow()
        {
            Expr left = ParseSignedValue();

            while (AllowsFunctionItems && Current.Kind == XPathTokenKind.Arrow)
            {
                m_index++;
                XPathToken target = Current;

                switch (target.Kind)
                {
                    // A name is the function itself, so the call is built as if it had been written out —
                    // which is what makes '=> concat(...)' reach the same overload resolution.
                    case XPathTokenKind.Name:
                        m_index++;
                        left = CreateCall(
                            target,
                            new List<Expr>(RequireNoPlaceholders(ParseArgumentList(left), "an '=>' call")));

                        break;

                    // A variable or a parenthesized expression yields the function at run time instead.
                    case XPathTokenKind.Variable:
                    case XPathTokenKind.LeftParen:
                    {
                        Expr function = ParsePrimaryExpression();
                        left = new DynamicCallExpr(
                            function,
                            RequireNoPlaceholders(ParseArgumentList(left), "an '=>' call"));

                        break;
                    }

                    default:
                        throw Error(
                            target, "'=>' is followed by a function name, a variable or a parenthesized "
                            + "expression.");
                }
            }

            return left;
        }

        /// <summary>
        /// Parses <c>("-" | "+")* ValueExpr</c>, which is where XPath 2.0 puts a leading sign.
        /// </summary>
        /// <remarks>
        /// The signs stack, and a leading plus is an operator rather than part of how the number is spelled:
        /// it takes a number and gives it back, which means it also refuses anything that is not one.
        /// </remarks>
        private Expr ParseSignedValue()
        {
            if (m_context.LegacySyntax)
            {
                return ParseSimpleMap();
            }

            // Counted rather than recursed over, for the reason ParseUnary gives. Every sign requires a
            // numeric operand and a plus then hands it back unchanged, so a run comes to the innermost sign,
            // which is the one that reports an operand that is not a number, negated once more if the
            // minuses outside it are odd in number.
            int signs = 0;
            int minuses = 0;
            XPathTokenKind innermost = XPathTokenKind.Plus;

            while (Current.Kind is XPathTokenKind.Minus or XPathTokenKind.Plus)
            {
                innermost = Current.Kind;
                minuses += innermost == XPathTokenKind.Minus ? 1 : 0;
                signs++;
                m_index++;
            }

            if (signs == 0)
            {
                return ParseSimpleMap();
            }

            Expr signed = innermost == XPathTokenKind.Minus
                ? new NegateExpr(ParseSimpleMap(), m_context.Version)
                : new UnaryPlusExpr(ParseSimpleMap());

            int outerMinuses = innermost == XPathTokenKind.Minus ? minuses - 1 : minuses;
            return outerMinuses % 2 == 1 ? new NegateExpr(signed, m_context.Version) : signed;
        }

        /// <summary>
        /// Parses <c>E ! F</c>, which binds tighter than everything but a path.
        /// </summary>
        /// <remarks>
        /// Where the grammar puts it, and it matters: <c>a ! b + 1</c> is <c>(a ! b) + 1</c>, so the operator
        /// applies to a step rather than to whatever expression happens to follow it.
        /// </remarks>
        private Expr ParseSimpleMap()
        {
            Expr left = ParsePath();

            while (IsXPath30 && Current.Kind == XPathTokenKind.Bang)
            {
                m_index++;
                left = new SimpleMapExpr(left, ParsePath());
            }

            return left;
        }

        /// <summary>
        /// Consumes a word if it is the keyword expected and an operator can appear here.
        /// </summary>
        /// <remarks>
        /// These words are only operators after an expression and before what they need — an element may be
        /// called <c>cast</c>, and <c>instance</c> alone is a name. Requiring the word that follows keeps them
        /// apart, so a path like <c>/cast</c> is untouched.
        /// </remarks>
        private bool TakeKeyword(string keyword)
        {
            if (m_context.LegacySyntax
                || Current.Kind != XPathTokenKind.Name
                || Current.Prefix.Length != 0
                || Current.Text != keyword)
            {
                return false;
            }

            string follower = keyword == "instance" ? "of" : "as";
            if (Peek(1).Kind != XPathTokenKind.Name || Peek(1).Text != follower)
            {
                return false;
            }

            m_index++;
            return true;
        }

        /// <summary>
        /// Parses the type after <c>cast as</c>, which is one atomic type and an optional <c>?</c>: a
        /// built-in type, or failing that a simple type the in-scope schema components have.
        /// </summary>
        private (XdmType.BuiltInType? Type, XdmSchemaType? SchemaType, bool AllowEmpty) ParseSingleType()
        {
            XPathToken token = Current;

            if (token.Kind != XPathTokenKind.Name)
            {
                throw Error(token, "A type name was expected.");
            }

            m_index++;
            bool schemaType = TypeNamespaceOf(token) == XdmType.SchemaNamespace;

            // Four names are types a value may be an instance of but never a type a cast may name: each
            // stands for a branch of the hierarchy rather than for anything a value can be made into. Checked
            // before the lookup below, so that the answer does not depend on which of them this engine
            // happens to have an entry for.
            if (schemaType && token.Text is "anyAtomicType" or "anySimpleType" or "untyped" or "NOTATION")
            {
                throw Error(
                    token,
                    XsltErrorCode.XPST0080,
                    $"xs:{token.Text} cannot be cast to. It names a family of types rather than one a value "
                    + "can be converted into.");
            }

            // A list type is a cast target and nothing else, so it is looked for here and not among the
            // types an expression may test against: xs:NMTOKENS names a sequence rather than an item.
            XdmType.BuiltInType? builtIn = null;
            XdmSchemaType? fromSchema = null;

            if (schemaType
                && (XdmType.TryGet(token.Text, out XdmType.BuiltInType type)
                    || XdmType.TryGetList(token.Text, out type)))
            {
                builtIn = type;
            }
            else if (TypeNamespaceOf(token) is string uri
                && m_context is ISchemaTypeProvider provider
                && provider.ResolveSchemaType(uri, token.Text) is XdmSchemaType found)
            {
                // A simple type a schema defines is a cast target as the built-in ones are; a complex type
                // has no values for a cast to make.
                if (found.Variety == XdmSchemaVariety.Complex)
                {
                    throw Error(
                        token,
                        XsltErrorCode.XPST0051,
                        $"'{token.Text}' is a complex type, and a cast names a simple one.");
                }

                fromSchema = found;
            }
            else
            {
                throw Error(
                    token, XsltErrorCode.XPST0051, $"'{token.Text}' is not a type this engine can construct.");
            }

            bool allowEmpty = Current.Kind == XPathTokenKind.Question;
            if (allowEmpty)
            {
                m_index++;
            }

            return (builtIn, fromSchema, allowEmpty);
        }

        /// <summary>Parses the type after <c>instance of</c> or <c>treat as</c>.</summary>
        private XdmSequenceType ParseSequenceType(bool itemOnly = false)
        {
            // array(array(…)) and map(K, map(K, …)) nest here directly, with no expression between the
            // levels to pass the guard in ParseExprSingle.
            RefuseDeepNesting();

            XPathToken token = Current;

            // '(T)' is T with an occurrence indicator of its own outside the parentheses, which is what lets
            // a function type take one: 'function(element()) as xs:string' followed by '?' would otherwise
            // hand the '?' to the result type.
            if (token.Kind == XPathTokenKind.LeftParen && AllowsFunctionItems)
            {
                m_index++;
                XdmSequenceType inner = ParseSequenceType(itemOnly: true);
                Expect(XPathTokenKind.RightParen);

                XdmOccurrence wrapped = ParseOccurrence(itemOnly);
                string indicator = wrapped switch
                {
                    XdmOccurrence.ZeroOrOne => "?",
                    XdmOccurrence.ZeroOrMore => "*",
                    XdmOccurrence.OneOrMore => "+",
                    _ => string.Empty,
                };

                return inner.WithOccurrence(wrapped, $"({inner}){indicator}");
            }

            if (token.Kind != XPathTokenKind.Name)
            {
                throw Error(token, "A type was expected.");
            }

            m_index++;

            if (token.Prefix.Length == 0 && token.Text == "empty-sequence"
                && Current.Kind == XPathTokenKind.LeftParen)
            {
                m_index++;
                Expect(XPathTokenKind.RightParen);
                return XdmSequenceType.EmptySequence;
            }

            // map(*) and array(*), which say the item is one of those and nothing about what it holds.
            if (AllowsMapsAndArrays && token.Prefix.Length == 0 && token.Text is "map" or "array"
                && Current.Kind == XPathTokenKind.LeftParen)
            {
                m_index++;
                XPathValueKind wanted = token.Text == "map" ? XPathValueKind.Map : XPathValueKind.Array;

                if (Current.Kind is XPathTokenKind.Star or XPathTokenKind.Multiply)
                {
                    m_index++;
                    Expect(XPathTokenKind.RightParen);
                    return XdmSequenceType.MapOrArray(wanted, ParseOccurrence(itemOnly), token.Text + "(*)");
                }

                // 'array(T)' says every member is a T and 'map(K, V)' says the same of every entry, the key
                // half included. Both are answerable here without any type annotation, because a map and an
                // array hold what they hold: the question is asked of the values themselves.
                if (wanted == XPathValueKind.Array)
                {
                    XdmSequenceType member = ParseSequenceType();
                    Expect(XPathTokenKind.RightParen);

                    return XdmSequenceType.MapOrArray(
                        wanted, ParseOccurrence(itemOnly), $"array({member})", member);
                }

                // A key is one atomic value, so K is an item type with no occurrence indicator to give it.
                XdmSequenceType key = ParseSequenceType(itemOnly: true);
                Expect(XPathTokenKind.Comma);
                XdmSequenceType value = ParseSequenceType();
                Expect(XPathTokenKind.RightParen);

                return XdmSequenceType.MapOrArray(
                    wanted, ParseOccurrence(itemOnly), $"map({key}, {value})", value, key);
            }

            // function(*), and function(T, …) as T, which says only how many arguments are wanted.
            if (AllowsFunctionItems && token.Prefix.Length == 0 && token.Text == "function"
                && Current.Kind == XPathTokenKind.LeftParen)
            {
                return ParseFunctionType(itemOnly);
            }

            // The kind tests that carry a name are parsed by the same routine a location step uses, so
            // 'instance of element(x)' asks exactly what 'child::element(x)' asks.
            if (token.Prefix.Length == 0 && s_kindTests.Contains(token.Text)
                && Current.Kind == XPathTokenKind.LeftParen)
            {
                m_index--;
                KindNodeTest kindTest = ParseKindTest(token);

                return XdmSequenceType.Kind(kindTest, ParseOccurrence(itemOnly), kindTest.ToString());
            }

            // A kind test is a name followed by parentheses; an atomic type is a name alone.
            bool isKindTest = Current.Kind == XPathTokenKind.LeftParen;
            if (isKindTest)
            {
                m_index++;
                Expect(XPathTokenKind.RightParen);
            }

            XdmOccurrence occurrence = ParseOccurrence(itemOnly);

            if (!isKindTest)
            {
                if (TypeNamespaceOf(token) != XdmType.SchemaNamespace
                    || !XdmType.TryGet(token.Text, out XdmType.BuiltInType atomic))
                {
                    // Not a built-in type: a type from a schema, where the context has schema components.
                    // An atomic type or a union of atomic types names values an item can be; a list type
                    // or a complex type does not, and a sequence type cannot name one (XPath 3.1 §2.5.4).
                    if (TypeNamespaceOf(token) is string uri
                        && m_context is ISchemaTypeProvider provider
                        && provider.ResolveSchemaType(uri, token.Text) is XdmSchemaType fromSchema)
                    {
                        if (fromSchema.Variety is XdmSchemaVariety.Atomic or XdmSchemaVariety.Union)
                        {
                            return XdmSequenceType.Schema(fromSchema, occurrence, TypeNameWritten(token));
                        }

                        throw Error(
                            token,
                            XsltErrorCode.XPST0051,
                            $"'{token.Text}' is a {(fromSchema.Variety == XdmSchemaVariety.List ? "list" : "complex")} "
                            + "type, and a sequence type names an atomic type or a union of them.");
                    }

                    throw Error(
                        token, XsltErrorCode.XPST0051, $"'{token.Text}' is not a type this engine knows.");
                }

                // Every atomic type is derived from xs:anyAtomicType, so the name matches any of them
                // rather than the one representation it shares with the string types.
                if (token.Text == "anyAtomicType")
                {
                    return XdmSequenceType.AnyAtomic(occurrence);
                }

                // And xs:numeric names three of them at once, so it asks which type a value has rather than
                // matching the one code the union itself was given.
                if (token.Text == "numeric")
                {
                    return XdmSequenceType.AnyNumeric(occurrence);
                }

                return XdmSequenceType.Atomic(
                    atomic.Code, occurrence, $"xs:{token.Text}", atomic.Derived, atomic);
            }

            if (token.Text == "item")
            {
                return XdmSequenceType.AnyItem(occurrence);
            }

            NodeKind? kind = token.Text switch
            {
                "node" => null,
                "element" => NodeKind.Element,
                "attribute" => NodeKind.Attribute,
                "text" => NodeKind.Text,
                "comment" => NodeKind.Comment,
                "processing-instruction" => NodeKind.ProcessingInstruction,
                "namespace-node" => NodeKind.Namespace,
                "document-node" => NodeKind.Root,
                _ => throw Error(token, XsltErrorCode.XPST0051, $"'{token.Text}()' is not a kind of node."),
            };

            return XdmSequenceType.Node(kind, occurrence, $"{token.Text}()");
        }

        /// <summary>
        /// Parses <c>function(*)</c> or a written-out signature, the name taken and the <c>(</c> current.
        /// </summary>
        /// <remarks>
        /// A signature is kept whole: the argument types, the result type and the arity they imply. What is
        /// done with them is the subtype judgement, which compares them against the types a function item
        /// was declared with — and admits an item that records none, since there the answer would have to
        /// be invented and inventing "no" refuses working stylesheets.
        /// </remarks>
        private XdmSequenceType ParseFunctionType(bool itemOnly)
        {
            m_index++;

            if (Current.Kind is XPathTokenKind.Star or XPathTokenKind.Multiply)
            {
                m_index++;
                Expect(XPathTokenKind.RightParen);
                return XdmSequenceType.FunctionItem(null, ParseOccurrence(itemOnly), "function(*)");
            }

            List<XdmSequenceType> parameters = new List<XdmSequenceType>();

            if (Current.Kind != XPathTokenKind.RightParen)
            {
                while (true)
                {
                    parameters.Add(ParseSequenceType());

                    if (Current.Kind != XPathTokenKind.Comma)
                    {
                        break;
                    }

                    m_index++;
                }
            }

            Expect(XPathTokenKind.RightParen);

            // The specification requires the result type on this form, unlike on function(*).
            ExpectKeyword("as");
            XdmSequenceType result = ParseSequenceType();

            return XdmSequenceType.FunctionItem(
                parameters.Count,
                ParseOccurrence(itemOnly),
                $"function({string.Join(", ", parameters)}) as {result}",
                parameters.ToArray(),
                result);
        }

        private XdmOccurrence ParseOccurrence(bool itemOnly)
        {
            // The key half of map(K, V) is an item type. Reading an indicator there and treating it as
            // always satisfied would accept 'map(xs:string+, …)', which the grammar has no production for.
            if (itemOnly)
            {
                return Current.Kind is XPathTokenKind.Question or XPathTokenKind.Star
                    or XPathTokenKind.Multiply or XPathTokenKind.Plus
                        ? throw Error(
                            Current,
                            XsltErrorCode.XPST0003,
                            "A map key is one item, so its type takes no occurrence indicator.")
                        : XdmOccurrence.One;
            }

            switch (Current.Kind)
            {
                case XPathTokenKind.Question:
                    m_index++;
                    return XdmOccurrence.ZeroOrOne;

                // A '*' after a type name follows an expression, so the scanner reads it as multiplication.
                // There is nothing to multiply here, so both spellings mean the occurrence indicator.
                case XPathTokenKind.Star:
                case XPathTokenKind.Multiply:
                    m_index++;
                    return XdmOccurrence.ZeroOrMore;

                case XPathTokenKind.Plus:
                    m_index++;
                    return XdmOccurrence.OneOrMore;

                default:
                    return XdmOccurrence.One;
            }
        }

        private Expr ParsePath()
        {
            if (!StartsPrimaryExpression())
            {
                return ParseLocationPath();
            }

            Expr primary = ParseFilterExpression();

            if (Current.Kind is not (XPathTokenKind.Slash or XPathTokenKind.DoubleSlash))
            {
                return primary;
            }

            return ContinuePath(primary, new List<AxisStep>());
        }

        /// <summary>
        /// Whether the syntax XPath 3.1 adds for maps and arrays is available here.
        /// </summary>
        /// <remarks>
        /// It is not syntax a 2.0 expression may use, and saying so matters rather than being pedantry:
        /// <c>[1]</c> alone is a syntax error in 2.0, and reading it as an array would take an error away
        /// from a stylesheet that had written a predicate with nothing in front of it.
        /// </remarks>
        private bool AllowsMapsAndArrays => IsXPath30;

        /// <summary>
        /// Whether the syntax XPath 3.0 adds for function items is available here.
        /// </summary>
        /// <remarks>
        /// Gated for the same reason as the map and array syntax, and the argument is sharper here:
        /// <c>$f(1)</c> is a syntax error in XPath 2.0, so reading it as a call would take an error away from
        /// a 2.0 stylesheet that had written a variable and a parenthesis by accident.
        /// </remarks>
        private bool AllowsFunctionItems => IsXPath30;

        /// <summary>
        /// Whether the expression being parsed is read at XPath 3.0 or later.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Named separately from the two properties above so that each use site says what it is gating rather
        /// than a version number, which is what makes a gate reviewable: the reason <c>[1]</c> is refused at
        /// 2.0 is not the reason <c>head()</c> is.
        /// </para>
        /// <para>
        /// Either version answering yes is enough, and the two say yes for different reasons. A processor
        /// that implements 3.0 has the 3.0 language whatever the stylesheet claims — XSLT 3.0 §2.3, and what
        /// the <c>version</c> attribute asks for is backwards-compatible <em>behaviour</em>, how a value
        /// converts and what a picture string means, rather than a smaller library. The conformance suite
        /// settles that direction: the tests for <c>fn:path</c> are written in a <c>version="2.0"</c>
        /// stylesheet and expect the function to be there.
        /// </para>
        /// <para>
        /// The other direction is this engine's own offer rather than the specification's. A stylesheet
        /// saying 3.0 on a processor asked to be 2.0 is read forwards-compatibly here, and where a construct
        /// it names is one this engine has, refusing it would help nobody: the stylesheet asked for 3.0 and
        /// 3.0 is what it gets. That is what let XPath 3.0 be used while <c>XsltVersion.Implemented</c>
        /// still said 2.0, and what keeps it usable for a caller who asks for 2.0 now that it says 3.0.
        /// </para>
        /// </remarks>
        private bool IsXPath30 =>
            m_context.Version.CompareTo(XsltVersion.V30) >= 0
            || m_context.SyntaxVersion.CompareTo(XsltVersion.V30) >= 0;

        private Expr ParseFilterExpression()
        {
            Expr result = ParsePrimaryExpression();
            List<Expr>? predicates = null;

            // Predicates, lookups and argument lists all bind after the primary and can alternate —
            // $a?b[1]?c, $f(1)(2) — so the predicates gathered so far are closed over whenever one of the
            // others interrupts them.
            while (true)
            {
                if (Current.Kind == XPathTokenKind.LeftBracket)
                {
                    predicates ??= new List<Expr>();
                    predicates.Add(ParsePredicate());
                    continue;
                }

                if (Current.Kind == XPathTokenKind.Question && AllowsMapsAndArrays)
                {
                    result = CloseFilter(result, ref predicates);
                    m_index++;
                    result = ParseLookup(result);
                    continue;
                }

                // A '(' after a complete expression is a call on whatever that expression produced. Nothing
                // in XPath 2.0 reads that way, which is why it is safe to take it here.
                if (Current.Kind == XPathTokenKind.LeftParen && AllowsFunctionItems)
                {
                    result = CloseFilter(result, ref predicates);

                    List<Expr?> given = ParseArgumentList(null);
                    result = HasPlaceholder(given)
                        ? new PartialApplicationExpr(result, given.ToArray())
                        : new DynamicCallExpr(result, given.ToArray()!);

                    continue;
                }

                break;
            }

            return CloseFilter(result, ref predicates);
        }

        /// <summary>
        /// Parses <c>function($x as xs:integer, $y) as item()* { … }</c>, the name not yet taken.
        /// </summary>
        /// <remarks>
        /// The parameters are bound as range variables, so the body reads them exactly as it reads a
        /// <c>for</c> variable, and the outer variables the body also reads keep the slots they already had.
        /// That is what makes the closure a closure.
        /// </remarks>
        private Expr ParseInlineFunction()
        {
            m_index++;
            Expect(XPathTokenKind.LeftParen);

            int parameterBase = m_rangeScope.Count;
            List<string> names = new List<string>();
            List<XdmSequenceType?> types = new List<XdmSequenceType?>();
            List<(string Uri, string Local)> parameters = new List<(string, string)>();

            if (Current.Kind != XPathTokenKind.RightParen)
            {
                while (true)
                {
                    XPathToken parameter = Current;
                    if (parameter.Kind != XPathTokenKind.Variable)
                    {
                        throw Error(parameter, "A parameter of an inline function is written '$name'.");
                    }

                    m_index++;
                    names.Add(parameter.Text);
                    types.Add(TakeAsType());
                    parameters.Add((
                        parameter.Prefix.Length == 0 ? string.Empty : ResolvePrefixOrThrow(parameter),
                        parameter.Text));

                    if (Current.Kind != XPathTokenKind.Comma)
                    {
                        break;
                    }

                    m_index++;
                }
            }

            Expect(XPathTokenKind.RightParen);

            // The parameters come into scope together, after the list is read: a later parameter's declared
            // type cannot mention an earlier one, so there is nothing to bind while the list is being parsed.
            foreach ((string uri, string local) in parameters)
            {
                m_rangeScope.Add((uri, local));
                m_rangeHighWater = Math.Max(m_rangeHighWater, m_rangeScope.Count);
            }

            XdmSequenceType? resultType = TakeAsType();

            Expect(XPathTokenKind.LeftBrace);
            Expr body = Current.Kind == XPathTokenKind.RightBrace ? new EmptySequenceExpr() : ParseExpression();
            Expect(XPathTokenKind.RightBrace);

            m_rangeScope.RemoveRange(parameterBase, m_rangeScope.Count - parameterBase);

            return new InlineFunctionExpr(
                parameterBase,
                names.ToArray(),
                types.ToArray(),
                resultType,
                body,
                m_rangeHighWater);
        }

        /// <summary>Takes an <c>as</c> and the type after it, where one was written.</summary>
        private XdmSequenceType? TakeAsType()
        {
            if (Current.Kind != XPathTokenKind.Name || Current.Prefix.Length != 0 || Current.Text != "as")
            {
                return null;
            }

            m_index++;
            return ParseSequenceType();
        }

        /// <summary>
        /// Parses <c>fn:concat#3</c>: a function named and not called, which is a value.
        /// </summary>
        /// <remarks>
        /// The call the name stands for is compiled here, once, with a variable in each argument position.
        /// Calling the value later means filling those variables in — so a reference to a built-in, to a type
        /// constructor and to an <c>xsl:function</c> need nothing separate written for them.
        /// </remarks>
        private Expr ParseNamedFunctionReference(XPathToken token)
        {
            m_index += 2;

            XPathToken arity = Current;
            if (arity.Kind != XPathTokenKind.Number
                || arity.Number < 0
                || arity.Number != Math.Floor(arity.Number))
            {
                throw Error(arity, "A '#' is followed by how many arguments the function takes.");
            }

            // An arity beyond what this engine will build an item for. It has to be a large number rather
            // than a small one: fn:concat takes any number of arguments, so concat#123456 names a function
            // that exists, and the item is the call compiled with one argument position per unit of arity.
            // Past this the reference names nothing that could be built, which is what XPST0017 says.
            if (arity.Number > MaximumNamedArity)
            {
                throw Error(
                    arity,
                    XsltErrorCode.XPST0017,
                    $"A function item of arity {arity.Number} is more than this processor will build; "
                    + $"the limit is {MaximumNamedArity}.");
            }

            m_index++;
            return NamedFunctionItem(token, (int)arity.Number);
        }

        /// <summary>How many arguments a named function reference may ask for.</summary>
        private const int MaximumNamedArity = 1_000_000;

        /// <summary>
        /// The function item a name of a given arity stands for.
        /// </summary>
        /// <remarks>
        /// The body is the call itself, compiled over variable references in slots zero upwards, so the item
        /// reaches whichever library the name belongs to by the ordinary route. Shared with partial
        /// application, which needs exactly this item to apply.
        /// </remarks>
        /// <param name="token">The name as written, carrying its prefix and position.</param>
        /// <param name="count">How many arguments the item takes.</param>
        private Expr NamedFunctionItem(XPathToken token, int count)
        {
            Expr[] placeholders = new Expr[count];
            for (int i = 0; i < count; i++)
            {
                placeholders[i] = new VariableReferenceExpr(i, isGlobal: false, $"arg{i + 1}");
            }

            string uri = token.Prefix.Length == 0
                ? XdmType.FunctionNamespace
                : ResolvePrefixOrThrow(token);

            XdmQName name = new XdmQName(token.Prefix, uri, token.Text);

            // current() is the one function no focus can be captured for: what it reads is the item the
            // innermost instruction is processing, which is XSLT's context rather than XPath's, and a dynamic
            // call has none. The specification makes the call a dynamic error rather than the reference.
            if (uri == XdmType.FunctionNamespace && token.Text == "current" && count == 0)
            {
                return new TypedLiteralExpr(XPathValue.FromFunction(new XdmNamedFunction(
                    name,
                    0,
                    new RefusedCallExpr(
                        XsltErrorCode.XTDE1360,
                        "current() reads the item an instruction is processing, and a function item called "
                        + "dynamically is processing nothing. Write current() where it is needed."))));
            }

            // The same for what an instruction's group or merge group is: a dynamic call has no current
            // group, and the specification makes the call the error rather than the reference.
            if (uri == XdmType.FunctionNamespace
                && ((count == 0 && token.Text is "current-group" or "current-grouping-key" or "current-merge-key")
                    || (count <= 1 && token.Text == "current-merge-group")))
            {
                XsltErrorCode code = token.Text switch
                {
                    "current-group" => XsltErrorCode.XTDE1061,
                    "current-grouping-key" => XsltErrorCode.XTDE1071,
                    "current-merge-group" => XsltErrorCode.XTDE3480,
                    _ => XsltErrorCode.XTDE3490,
                };

                return new TypedLiteralExpr(XPathValue.FromFunction(new XdmNamedFunction(
                    name,
                    count,
                    new RefusedCallExpr(
                        code,
                        $"{token.Text}() reads what the innermost xsl:for-each-group or xsl:merge is "
                        + "processing, and a function item called dynamically is inside none. Write "
                        + $"{token.Text}() where it is needed."))));
            }

            Expr call = CreateCall(token, new List<Expr>(placeholders));

            // A function that reads the focus takes the focus of this very place with it, so the reference
            // is made where it is evaluated; so does one whose body is run by the transformation, which the
            // item may outlive. Every other one is a constant.
            return ReadsTheFocus(uri, token.Text, count) || call.NeedsTheTransformation
                ? new FocusCapturingFunctionExpr(name, count, call)
                : new TypedLiteralExpr(XPathValue.FromFunction(
                    new XdmNamedFunction(name, count, call) { DeclaredTypes = call.DeclaredSignature }));
        }

        /// <summary>
        /// Whether a function of the standard library reads the focus when called with this many arguments.
        /// </summary>
        /// <remarks>
        /// The zero-argument forms all do — <c>name()</c>, <c>string()</c>, <c>position()</c> — and a few
        /// take an argument and still read the context node or its document: <c>lang()</c>, <c>id()</c>,
        /// <c>key()</c>, and XSLT's <c>accumulator-before()</c>. Also <c>current-merge-group()</c>, which
        /// reads what <c>xsl:merge</c> put in the context.
        /// </remarks>
        private static bool ReadsTheFocus(string uri, string localName, int arity)
        {
            return uri == XdmType.FunctionNamespace
                && (arity == 0
                    || localName is "lang" or "id" or "idref" or "element-with-id" or "key" or "regex-group"
                        or "accumulator-before" or "accumulator-after" or "current-merge-group"
                        or "unparsed-entity-uri" or "unparsed-entity-public-id");
        }

        private Expr CloseFilter(Expr primary, ref List<Expr>? predicates)
        {
            if (predicates is null)
            {
                return primary;
            }

            Expr filtered = new FilterExpr(primary, predicates.ToArray(), m_context.Version);
            predicates = null;
            return filtered;
        }

        /// <summary>Parses the entries of a <c>map { … }</c>, the opening brace already taken.</summary>
        private Expr ParseCurlyMap()
        {
            List<Expr> keys = new List<Expr>();
            List<Expr> values = new List<Expr>();

            if (Current.Kind != XPathTokenKind.RightBrace)
            {
                while (true)
                {
                    keys.Add(ParseExprSingle());
                    Expect(XPathTokenKind.Colon);
                    values.Add(ParseExprSingle());

                    if (Current.Kind != XPathTokenKind.Comma)
                    {
                        break;
                    }

                    m_index++;
                }
            }

            Expect(XPathTokenKind.RightBrace);
            return new CurlyMapExpr(keys.ToArray(), values.ToArray());
        }

        /// <summary>Parses the contents of an <c>array { … }</c>, the opening brace already taken.</summary>
        private Expr ParseCurlyArray()
        {
            // One expression, however many commas are in it: every item it produces is a member. That is
            // what separates this from the square form, which takes one expression per member.
            Expr? content = Current.Kind == XPathTokenKind.RightBrace ? null : ParseExpression();

            Expect(XPathTokenKind.RightBrace);
            return new CurlyArrayExpr(content);
        }

        /// <summary>Parses what follows a <c>?</c>: a name, a number, <c>*</c>, or an expression.</summary>
        private Expr ParseLookup(Expr operand)
        {
            XPathToken token = Current;

            switch (token.Kind)
            {
                case XPathTokenKind.Star:
                case XPathTokenKind.Multiply:
                    m_index++;
                    return new LookupExpr(operand, LookupKind.Wildcard, default, null);

                // An NCName and nothing wider: a lookup key is a name with no namespace at all, so neither
                // 'p:x' nor 'Q{}x' is one, however empty the second one's namespace is.
                case XPathTokenKind.Name when token.Prefix.Length == 0 && token.NamespaceUri is null:
                    m_index++;
                    return new LookupExpr(
                        operand, LookupKind.Literal, XPathValue.FromString(token.Text), null);

                case XPathTokenKind.Number:
                {
                    m_index++;

                    // An integer literal, judged by how it is written and not by what it is worth: '?1.0'
                    // names no key even though the number is whole, the grammar asking for digits here.
                    if (!IsIntegerLiteral(token.Text) || double.IsInfinity(token.Number))
                    {
                        throw Error(token, "A '?' takes a whole number, a name, '*' or an expression.");
                    }

                    return new LookupExpr(
                        operand, LookupKind.Literal, XPathValue.FromInteger((long)token.Number), null);
                }

                case XPathTokenKind.LeftParen:
                {
                    m_index++;

                    // '?()' looks up nothing, which is not the same as being malformed: the key expression
                    // is an empty sequence and the lookup answers with one.
                    Expr key = Current.Kind == XPathTokenKind.RightParen
                        ? new SequenceExpr(Array.Empty<Expr>())
                        : ParseExpression();

                    Expect(XPathTokenKind.RightParen);
                    return new LookupExpr(operand, LookupKind.Computed, default, key);
                }

                default:
                    throw Error(token, "A '?' takes a name, a whole number, '*' or a parenthesized expression.");
            }
        }

        /// <summary>Whether a number was written as an integer literal, which is digits and nothing else.</summary>
        private static bool IsIntegerLiteral(string text)
        {
            foreach (char character in text)
            {
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }
            }

            return text.Length > 0;
        }

        /// <summary>Whether an integer literal is one this engine's <c>xs:integer</c> can hold.</summary>
        private static bool FitsInteger(string text)
        {
            return long.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long _);
        }

        private Expr ParsePrimaryExpression()
        {
            XPathToken token = Current;

            switch (token.Kind)
            {
                case XPathTokenKind.Dot:
                    m_index++;
                    return new ContextItemExpr(m_context.Version);

                // The unary lookup, which is the binary one with the context item on its left: inside a
                // predicate over a sequence of maps, '?name' asks each of them in turn.
                case XPathTokenKind.Question when AllowsMapsAndArrays:
                    m_index++;
                    return ParseLookup(new ContextItemExpr(m_context.Version));

                case XPathTokenKind.Variable:
                {
                    m_index++;
                    string uri = token.Prefix.Length == 0 ? string.Empty : ResolvePrefixOrThrow(token);

                    // A variable bound by an enclosing for, some or every shadows anything the host declared,
                    // and the innermost binding of a name wins.
                    for (int i = m_rangeScope.Count - 1; i >= 0; i--)
                    {
                        if (m_rangeScope[i].Local == token.Text && m_rangeScope[i].Uri == uri)
                        {
                            return new RangeVariableExpr(i, token.Text);
                        }
                    }

                    // A static variable is settled before the expression naming it is compiled, so it stands
                    // in as its own value rather than as a lookup. Asked first: it is also the only kind of
                    // variable a use-when can see, there being no slots yet where one is answered.
                    if (m_context.TryResolveStaticVariable(uri, token.Text) is XPathValue constant)
                    {
                        return new ConstantExpr(constant);
                    }

                    if (!m_context.TryResolveVariable(uri, token.Text, out int slot, out bool isGlobal))
                    {
                        throw Error(token, XsltErrorCode.XPST0008, $"Variable '${token.Text}' is not in scope.");
                    }

                    return new VariableReferenceExpr(slot, isGlobal, token.Text);
                }

                case XPathTokenKind.Name when AllowsMapsAndArrays
                    && token.Prefix.Length == 0
                    && token.Text is "map" or "array"
                    && Peek(1).Kind == XPathTokenKind.LeftBrace:
                {
                    // Only where a brace follows, so an element may still be called map or array and a path
                    // still reaches it — the same rule that lets 'if' be an element name.
                    m_index += 2;
                    return token.Text == "map" ? ParseCurlyMap() : ParseCurlyArray();
                }

                case XPathTokenKind.Name when AllowsFunctionItems
                    && token.Prefix.Length == 0
                    && token.Text == "function"
                    && Peek(1).Kind == XPathTokenKind.LeftParen:
                    return ParseInlineFunction();

                case XPathTokenKind.Name when AllowsFunctionItems
                    && Peek(1).Kind == XPathTokenKind.Hash:
                    return ParseNamedFunctionReference(token);

                case XPathTokenKind.LeftBracket when AllowsMapsAndArrays:
                {
                    // A '[' where an expression is expected is an array; one after an expression is a
                    // predicate, and the two never compete because a predicate has something to filter.
                    m_index++;
                    List<Expr> members = new List<Expr>();

                    if (Current.Kind != XPathTokenKind.RightBracket)
                    {
                        members.Add(ParseExprSingle());
                        while (Current.Kind == XPathTokenKind.Comma)
                        {
                            m_index++;
                            members.Add(ParseExprSingle());
                        }
                    }

                    Expect(XPathTokenKind.RightBracket);
                    return new SquareArrayExpr(members.ToArray());
                }

                case XPathTokenKind.LeftParen:
                {
                    m_index++;

                    if (Current.Kind == XPathTokenKind.RightParen)
                    {
                        // "()" is the empty sequence in XPath 2.0, and a syntax error before it.
                        m_index++;
                        if (m_context.LegacySyntax)
                        {
                            throw Error(token, "An expression was expected between the parentheses.");
                        }

                        return new EmptySequenceExpr();
                    }

                    Expr inner = ParseExpression();
                    Expect(XPathTokenKind.RightParen);
                    return inner;
                }

                case XPathTokenKind.StringLiteral:
                    m_index++;
                    return new StringLiteralExpr(token.Text);

                case XPathTokenKind.Number:
                    m_index++;

                    // XPath 1.0 has one numeric type, so how the literal was written does not matter. XPath
                    // 2.0 types it by its lexical form, which is what makes 1 div 2 exact and 1 idiv 2 legal.
                    if (m_context.LegacySyntax)
                    {
                        return new NumberLiteralExpr(token.Number);
                    }

                    if (!IsIntegerLiteral(token.Text) || FitsInteger(token.Text))
                    {
                        return new TypedLiteralExpr(TypedNumber(token));
                    }

                    // An integer literal past what this engine's xs:integer holds is an overflow rather than
                    // a double: reading it as one would answer with a different number than was written, and
                    // silently. The specification allows a bounded implementation and asks for FOAR0002.
                    //
                    // Except where the expression was written for XPath 1.0, which had no integers to
                    // overflow — the same digits meant a double there, and the specification leaves what a
                    // too-large literal becomes to the implementation. Refusing one a 1.0 stylesheet has
                    // always been allowed to write would be the compatibility mode failing at its one job.
                    return m_context.Version.IsBackwardsCompatible
                        ? new NumberLiteralExpr(token.Number)
                        : new OverflowingLiteralExpr(token.Text);

                case XPathTokenKind.Name:
                {
                    // A reserved name is one the grammar needs for a kind test or a keyword, so it cannot
                    // also be a function name. The call does not fail to resolve; it does not parse.
                    if (token.Prefix.Length == 0 && s_reservedFunctionNames.Contains(token.Text)
                        && !m_context.LegacySyntax)
                    {
                        throw Error(
                            token,
                            XsltErrorCode.XPST0003,
                            $"'{token.Text}' is a name the grammar reserves, so there is no function of it.");
                    }

                    m_index++;

                    List<Expr?> given = ParseArgumentList(null);

                    // A placeholder turns the call into a partial application of the function item the name
                    // stands for — the same item 'name#n' would have produced, so the two forms agree by
                    // construction rather than by two implementations happening to match.
                    return HasPlaceholder(given)
                        ? new PartialApplicationExpr(NamedFunctionItem(token, given.Count), given.ToArray())
                        : CreateCall(token, new List<Expr>(given!));
                }

                default:
                    throw Error(token, "Expected an expression.");
            }
        }

        /// <summary>
        /// Parses a parenthesized argument list, optionally with an argument already supplied.
        /// </summary>
        /// <param name="first">
        /// An argument to put in front of those written, which is how the arrow works: <c>$x =&gt; f(1)</c>
        /// is <c>f($x, 1)</c>, so the left operand arrives here rather than in the text being parsed.
        /// </param>
        /// <summary>
        /// Parses the arguments of a call, with <see langword="null"/> where a <c>?</c> stands.
        /// </summary>
        /// <remarks>
        /// A <c>?</c> in an argument list is XPath 3.1's placeholder: the call supplies every other argument
        /// and yields a function waiting for this one. It is recognised here rather than by the callers so
        /// that both the named and the dynamic form get it from one place.
        /// </remarks>
        /// <param name="first">An argument already parsed, which <c>=&gt;</c> supplies.</param>
        private List<Expr?> ParseArgumentList(Expr? first)
        {
            Expect(XPathTokenKind.LeftParen);

            List<Expr?> arguments = new List<Expr?>();
            if (first is not null)
            {
                arguments.Add(first);
            }

            if (Current.Kind != XPathTokenKind.RightParen)
            {
                arguments.Add(ParseArgument());
                while (Current.Kind == XPathTokenKind.Comma)
                {
                    m_index++;
                    arguments.Add(ParseArgument());
                }
            }

            Expect(XPathTokenKind.RightParen);
            return arguments;
        }

        /// <summary>One argument, or null where it is a placeholder.</summary>
        private Expr? ParseArgument()
        {
            // Only where the language has function items at all: at 2.0 a '?' here is a syntax error, and
            // reading it as a placeholder would accept a stylesheet a conformant 2.0 processor refuses.
            if (AllowsFunctionItems
                && Current.Kind == XPathTokenKind.Question
                && Peek(1).Kind is XPathTokenKind.Comma or XPathTokenKind.RightParen)
            {
                m_index++;
                return null;
            }

            return ParseExprSingle();
        }

        /// <summary>Whether any argument in a list was written as a placeholder.</summary>
        private static bool HasPlaceholder(List<Expr?> arguments)
        {
            foreach (Expr? argument in arguments)
            {
                if (argument is null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The arguments of a call that may not have placeholders, refusing one that does.
        /// </summary>
        /// <param name="arguments">The parsed arguments.</param>
        /// <param name="where">What was being called, for the message.</param>
        private Expr[] RequireNoPlaceholders(List<Expr?> arguments, string where)
        {
            if (!HasPlaceholder(arguments))
            {
                return arguments.ToArray()!;
            }

            throw Error(
                Current,
                $"A '?' cannot stand for an argument of {where}, which supplies one of its arguments itself.");
        }

        /// <summary>
        /// Builds a call to a function named in the text, whichever library it turns out to belong to.
        /// </summary>
        /// <param name="token">The name as written, which carries the prefix and the position.</param>
        /// <param name="arguments">The compiled arguments.</param>
        private Expr CreateCall(XPathToken token, List<Expr> arguments)
        {
            // A name written as Q{uri}local goes to the host in that form, its namespace being nothing a
            // prefix lookup could find — the host resolves both spellings.
            string name = token.NamespaceUri is string braced
                ? $"Q{{{braced}}}{token.Text}"
                : token.Prefix.Length == 0 ? token.Text : $"{token.Prefix}:{token.Text}";

            try
            {
                if (TryCreateNamespacedFunction(token, arguments) is Expr namespaced)
                {
                    return namespaced;
                }

                // The host gets first refusal, so XSLT's own functions can reach the stylesheet.
                return m_context.TryCreateFunction(name, arguments.ToArray())
                    ?? CreateLibraryFunction(name, arguments.ToArray());
            }
            catch (XsltException exception)
            {
                throw Locate(token, exception);
            }
        }

        /// <summary>
        /// Types a numeric literal by how it was written, which is what XPath 2.0 decides its type from.
        /// </summary>
        /// <remarks>
        /// Read from the text rather than from the scanner's double. An <c>xs:integer</c> holds eighteen
        /// digits and a double fifteen, so <c>999999999999999999</c> and <c>999999999999999998</c> come out
        /// of a double as the same value — and the whole point of holding integers exactly is lost at the one
        /// place a large one is most likely to be written.
        /// </remarks>
        private static XPathValue TypedNumber(XPathToken token)
        {
            string text = token.Text;

            // An exponent makes it an xs:double whatever else it looks like.
            if (text.Contains('e', StringComparison.OrdinalIgnoreCase))
            {
                return XPathValue.FromNumber(token.Number);
            }

            if (text.Contains('.'))
            {
                return decimal.TryParse(
                    text,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal exact)
                        ? XPathValue.FromDecimal(exact)
                        : XPathValue.FromNumber(token.Number);
            }

            // A literal beyond what a long holds is outside this engine's xs:integer, and becomes a double
            // rather than being refused — the value is still the one that was written, to the digits a double
            // has, which is what a 1.0 processor would have given anyway.
            return long.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long integer)
                    ? XPathValue.FromInteger(integer)
                    : XPathValue.FromNumber(token.Number);
        }

        /// <summary>
        /// Resolves a call whose name is prefixed, when the prefix names one of the namespaces XPath 2.0
        /// gives a meaning of its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two namespaces matter. A name in the XML Schema namespace is a type constructor: <c>xs:integer(…)</c>
        /// is not a function anyone declared but the type itself, used to cast. A name in the function
        /// namespace is a core function written out in full, so <c>fn:count(…)</c> and <c>count(…)</c> are the
        /// same call — XPath 2.0 puts the core library in a namespace and lets it be named either way.
        /// </para>
        /// <para>
        /// Returning <see langword="null"/> leaves the name to the host and then to the core library, which is
        /// what happens for every unprefixed call and for extension functions.
        /// </para>
        /// </remarks>
        private Expr? TryCreateNamespacedFunction(XPathToken token, List<Expr> arguments)
        {
            if (token.Prefix.Length == 0)
            {
                return null;
            }

            string? uri = NamespaceOf(token);

            // A prefix that stands for nothing is the first thing wrong here, and saying the function is
            // unknown would send the reader looking for a function rather than for the binding.
            if (uri is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0081,
                    $"The prefix '{token.Prefix}' in '{token.Prefix}:{token.Text}()' is not bound to a "
                    + "namespace.");
            }

            if (uri == XdmType.FunctionNamespace)
            {
                return CreateLibraryFunction(token.Text, arguments.ToArray());
            }

            // A prefix means whatever it meant where it was written, and nothing at run time knows that. So a
            // QName built from a literal is resolved here, where the bindings are still in scope; anything
            // else reaches the constructor, which accepts only names that need no resolving.
            if (uri == XdmType.SchemaNamespace
                && XdmType.HasConstructor(token.Text)
                && arguments.Count == 1
                && arguments[0] is StringLiteralExpr literal
                && XdmType.TryGet(token.Text, out XdmType.BuiltInType type)
                && type.Code == XdmTypeCode.QName)
            {
                return new TypedLiteralExpr(ResolveLiteralQName(literal.Value));
            }

            if (uri is XdmType.SchemaNamespace
                or MapArrayFunctionExpr.MapNamespace
                or MapArrayFunctionExpr.ArrayNamespace
                or Xpath30FunctionExpr.MathNamespace)
            {
                return WithDefaultCollation(FunctionLibrary.TryCreate(
                    uri,
                    token.Text,
                    arguments.ToArray(),
                    m_context.Version,
                    m_context.SyntaxVersion,
                    m_context.LegacySyntax,
                    m_context.InScopeNamespaces));
            }

            // A simple type a schema defines is a constructor function of its own name, as the built-in
            // types are: one argument, the empty sequence admitted, and the cast is the whole of it.
            if (arguments.Count == 1
                && m_context is ISchemaTypeProvider provider
                && provider.ResolveSchemaType(uri, token.Text) is { Variety: not XdmSchemaVariety.Complex } constructed)
            {
                return new CastExpr(
                    arguments[0], constructed, allowEmpty: true, testOnly: false, m_context.InScopeNamespaces);
            }

            return null;
        }

        private XPathValue ResolveLiteralQName(string text)
        {
            if (!XdmQName.TrySplit(text, out string prefix, out string localName))
            {
                throw XsltErrors.Error(XsltErrorCode.FORG0001, $"'{text}' is not a valid xs:QName.");
            }

            if (prefix.Length == 0)
            {
                return XPathValue.FromQName(new XdmQName(string.Empty, string.Empty, localName));
            }

            string uri = m_context.ResolvePrefix(prefix)
                ?? throw XsltErrors.Error(
                    XsltErrorCode.FONS0004,
                    $"The prefix '{prefix}' in the QName '{text}' is not bound to a namespace here.");

            return XPathValue.FromQName(new XdmQName(prefix, uri, localName));
        }

        /// <summary>
        /// Resolves a name against the function library, which is the XPath 1.0 core plus, where the version
        /// asks for it, the functions XPath 2.0 adds.
        /// </summary>
        /// <remarks>
        /// The 2.0 functions are gated rather than always available, because a stylesheet written for 1.0
        /// that calls <c>matches()</c> means an extension function of its own and should be told so.
        /// </remarks>
        private Expr CreateLibraryFunction(string localName, Expr[] arguments)
        {
            // Answered before the library because it is XSLT's function before it is XPath's: the picture is
            // read against a decimal format the stylesheet declared, and the library knows of no stylesheet.
            if (IsXPath30 && localName == "format-number")
            {
                return CreateFormatNumber(arguments);
            }

            // The host gets first refusal here as it does for a bare name, so XSLT's own functions answer to
            // fn:current() and Q{…}available-system-properties() as they do to their bare spelling.
            return m_context.TryCreateFunction(localName, arguments)
                ?? WithDefaultCollation(FunctionLibrary.TryCreate(
                    XdmType.FunctionNamespace,
                    localName,
                    arguments,
                    m_context.Version,
                    m_context.SyntaxVersion,
                    m_context.LegacySyntax,
                    m_context.InScopeNamespaces))
                ?? throw XsltErrors.Error(XsltErrorCode.XPST0017, $"Unknown function '{localName}()'.");
        }

        /// <summary>
        /// Tells a library function which collation to use where its caller names none.
        /// </summary>
        /// <remarks>
        /// Only where a <c>default-collation</c> in scope says something other than the code point
        /// collation, which is the fallback the function already has — so the ordinary call is left exactly
        /// as it was built.
        /// </remarks>
        /// <param name="built">The call, which may be null or may be some other kind of expression.</param>
        private Expr? WithDefaultCollation(Expr? built)
        {
            if (m_context.DefaultCollation is not string collation
                || collation == Collation.CodepointUri)
            {
                return built;
            }

            switch (built)
            {
                case Xpath2FunctionExpr library:
                    library.UseDefaultCollation(collation, m_context.CollationResolver);
                    break;

                case FunctionCallExpr core:
                    core.DefaultCollation = Collation.Resolve(collation, m_context.CollationResolver);
                    break;
            }

            return built;
        }

        /// <summary>
        /// Creates a call to <c>fn:format-number()</c>, which XPath 3.0 moved into the core library.
        /// </summary>
        /// <remarks>
        /// It was an XSLT function and still is one — a stylesheet answers here first, through
        /// <see cref="IXPathStaticContext.TryCreateFunction"/>, because a stylesheet has more to say about a
        /// name than the static context alone. Outside one, the formats come from
        /// <see cref="IXPathStaticContext.ResolveDecimalFormat"/>, which by default knows only the unnamed one.
        /// </remarks>
        private Expr CreateFormatNumber(Expr[] arguments)
        {
            if (arguments.Length is < 2 or > 3)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'fn:format-number()' takes two or three arguments, and was given {arguments.Length}.");
            }

            // A written name is settled here, so that the picture can be read once against the format it
            // belongs to. A computed one cannot be, and nothing says a computed name has to be wrong, so it
            // waits until there is a value to look up.
            if (arguments.Length == 2 || arguments[2] is StringLiteralExpr)
            {
                string name = arguments.Length == 2 ? string.Empty : ((StringLiteralExpr)arguments[2]).Value;

                return new Compiler.FormatNumberExpr(
                    arguments[0], arguments[1], Resolve(name), m_context.Version, m_context.SyntaxVersion);
            }

            return new NamedDecimalFormatExpr(
                arguments[0], arguments[1], arguments[2], m_context, m_context.Version);
        }

        /// <summary>Finds the decimal format a name stands for, or raises <c>FODF1280</c>.</summary>
        private DecimalFormat Resolve(string name)
        {
            return NamedDecimalFormatExpr.Resolve(name, m_context);
        }

        private bool StartsPrimaryExpression()
        {
            switch (Current.Kind)
            {
                case XPathTokenKind.Variable:
                case XPathTokenKind.LeftParen:
                case XPathTokenKind.StringLiteral:
                case XPathTokenKind.Number:

                // '[' begins an array here rather than a predicate, because a predicate needs something in
                // front of it and this is asked only where an expression may start. '?' is the unary lookup
                // for the same reason: the binary one needs something in front of it too.
                case XPathTokenKind.LeftBracket when AllowsMapsAndArrays:
                case XPathTokenKind.Question when AllowsMapsAndArrays:

                // '.' is a primary expression and not a step, which is what lets it denote an atomic context
                // item. '..' stays the parent step it has always been.
                case XPathTokenKind.Dot:
                    return true;

                case XPathTokenKind.Name:
                    // 'map {' and 'array {' begin a constructor; without the brace they are ordinary names.
                    if (AllowsMapsAndArrays
                        && Current.Prefix.Length == 0
                        && Current.Text is "map" or "array"
                        && Peek(1).Kind == XPathTokenKind.LeftBrace)
                    {
                        return true;
                    }

                    // 'name#2' names a function without calling it, which is a value and so a primary
                    // expression. Nothing else puts a '#' after a name.
                    if (AllowsFunctionItems && Peek(1).Kind == XPathTokenKind.Hash)
                    {
                        return true;
                    }

                    // A name followed by '(' is a function call unless it is a kind test, which is part of a
                    // location step instead.
                    return Peek(1).Kind == XPathTokenKind.LeftParen
                        && (Current.Prefix.Length != 0
                            || !(s_nodeTypes.Contains(Current.Text) || s_kindTests.Contains(Current.Text)));

                default:
                    return false;
            }
        }

        private Expr ParseLocationPath()
        {
            List<AxisStep> steps = new List<AxisStep>();

            if (Current.Kind == XPathTokenKind.Slash)
            {
                m_index++;
                if (!StartsStep() && !StartsPrimaryExpression())
                {
                    // A lone '/' selects the root.
                    return new RootExpr();
                }

                return ContinueFirstStep(new RootExpr(), steps);
            }

            if (Current.Kind == XPathTokenKind.DoubleSlash)
            {
                m_index++;
                steps.Add(DescendantOrSelfStep());
                return ContinueFirstStep(new RootExpr(), steps);
            }

            return ContinueFirstStep(null, steps);
        }

        /// <summary>Takes the first step of a relative path, which may be an expression rather than an axis.</summary>
        private Expr ContinueFirstStep(Expr? start, List<AxisStep> steps)
        {
            if (StartsPrimaryExpression())
            {
                Expr source = new PathExpr(start, steps.ToArray());
                steps.Clear();

                return ContinuePath(new StepMapExpr(source, ParseFilterExpression()), steps);
            }

            steps.Add(ParseStep());
            return ContinuePath(start, steps);
        }

        /// <summary>
        /// Consumes the rest of a path.
        /// </summary>
        /// <remarks>
        /// XPath 2.0 lets a step be any primary expression — <c>employee/(status|overtime)/day</c>,
        /// <c>name/string()</c> — where 1.0 allowed only an axis step. The axis steps gathered so far become
        /// a <see cref="PathExpr"/>, which is the shape that walks axes quickly, and the general step maps
        /// over what it produced. So a path pays for the general form only where one was written.
        /// </remarks>
        /// <param name="start">What the path so far starts from, or <see langword="null"/> for the context item.</param>
        /// <param name="steps">The axis steps gathered so far, which this may add to and clear.</param>
        private Expr ContinuePath(Expr? start, List<AxisStep> steps)
        {
            while (true)
            {
                if (Current.Kind == XPathTokenKind.Slash)
                {
                    m_index++;
                }
                else if (Current.Kind == XPathTokenKind.DoubleSlash)
                {
                    m_index++;
                    steps.Add(DescendantOrSelfStep());
                }
                else
                {
                    break;
                }

                if (StartsPrimaryExpression())
                {
                    Expr source = new PathExpr(start, steps.ToArray());
                    steps.Clear();
                    start = new StepMapExpr(source, ParseFilterExpression());
                    continue;
                }

                steps.Add(ParseStep());
            }

            return steps.Count == 0 && start is not null ? start : new PathExpr(start, steps.ToArray());
        }

        private static AxisStep DescendantOrSelfStep()
        {
            return new AxisStep(Axis.DescendantOrSelf, NodeTest.AnyNode, Array.Empty<Expr>());
        }

        /// <summary>
        /// Whether a step begins here, which after a lone <c>/</c> is the question of what the slash was.
        /// </summary>
        /// <remarks>
        /// A word can begin a step or continue an expression, and only what follows it says which:
        /// <c>/instance</c> selects a child called <c>instance</c>, where <c>/ instance of node()</c> asks
        /// about the root. So a word that is acting as an operator does not begin a step.
        /// </remarks>
        private bool StartsStep()
        {
            if (Current.Kind is XPathTokenKind.Dot
                or XPathTokenKind.DoubleDot
                or XPathTokenKind.At
                or XPathTokenKind.Star)
            {
                return true;
            }

            return Current.Kind == XPathTokenKind.Name && !IsOperatorHere();
        }

        /// <summary>Whether the current word is being used as an operator rather than as a name.</summary>
        private bool IsOperatorHere()
        {
            if (m_context.LegacySyntax || Current.Prefix.Length != 0)
            {
                return false;
            }

            // The four type operators need the word after them, which is what tells a name from an operator.
            if (Current.Text is "instance" or "treat" or "castable" or "cast")
            {
                string follower = Current.Text == "instance" ? "of" : "as";
                return Peek(1).Kind == XPathTokenKind.Name && Peek(1).Text == follower;
            }

            return Current.Text is "and" or "or" or "div" or "idiv" or "mod" or "to" or "is"
                or "eq" or "ne" or "lt" or "le" or "gt" or "ge"
                or "union" or "intersect" or "except";
        }

        private AxisStep ParseStep()
        {
            if (Current.Kind == XPathTokenKind.Dot)
            {
                m_index++;
                return new AxisStep(Axis.Self, NodeTest.AnyNode, ParsePredicates());
            }

            if (Current.Kind == XPathTokenKind.DoubleDot)
            {
                m_index++;
                return new AxisStep(Axis.Parent, NodeTest.AnyNode, ParsePredicates());
            }

            Axis axis = Axis.Child;
            bool defaulted = true;

            if (Current.Kind == XPathTokenKind.At)
            {
                m_index++;
                axis = Axis.Attribute;
                defaulted = false;
            }
            else if (Current.Kind == XPathTokenKind.Name && Peek(1).Kind == XPathTokenKind.DoubleColon)
            {
                axis = ParseAxisName(Current);
                defaulted = false;
                m_index += 2;
            }

            NodeTest test = ParseNodeTest(axis);

            // attribute() with no axis written is a step on the attribute axis, and namespace-node() one on
            // the namespace axis (§3.3.2.1): each test names a kind that is on one axis only, so the step
            // selects those nodes and not a child that could never be one.
            if (defaulted && test is KindNodeTest { Kind: NodeKind.Attribute or NodeKind.Namespace } kindTest)
            {
                axis = kindTest.Kind == NodeKind.Attribute ? Axis.Attribute : Axis.Namespace;
            }

            return new AxisStep(axis, test, ParsePredicates());
        }

        private Axis ParseAxisName(XPathToken token)
        {
            if (token.Prefix.Length != 0)
            {
                throw Error(token, $"'{token.Prefix}:{token.Text}' is not an axis name.");
            }

            return token.Text switch
            {
                "self" => Axis.Self,
                "child" => Axis.Child,
                "parent" => Axis.Parent,
                "attribute" => Axis.Attribute,
                "descendant" => Axis.Descendant,
                "descendant-or-self" => Axis.DescendantOrSelf,
                "ancestor" => Axis.Ancestor,
                "ancestor-or-self" => Axis.AncestorOrSelf,
                "following" => Axis.Following,
                "following-sibling" => Axis.FollowingSibling,
                "preceding" => Axis.Preceding,
                "preceding-sibling" => Axis.PrecedingSibling,
                "namespace" => Axis.Namespace,
                _ => throw Error(token, $"'{token.Text}' is not an axis name."),
            };
        }

        private NodeTest ParseNodeTest(Axis axis)
        {
            XPathToken token = Current;

            if (token.Kind == XPathTokenKind.Star)
            {
                m_index++;
                return NodeTest.Wildcard;
            }

            if (token.Kind != XPathTokenKind.Name)
            {
                throw Error(token, "Expected a node test.");
            }

            if (token.Text == "*")
            {
                // prefix:* — every name in one namespace, or Q{uri}* with the namespace written out.
                m_index++;
                string uri = token.NamespaceUri ?? ResolvePrefixOrThrow(token);
                return new NamespaceWildcardNodeTest(
                    uri, token.NamespaceUri is null ? $"{token.Prefix}:*" : $"Q{{{uri}}}*");
            }

            if (token.Prefix == "*")
            {
                // *:local — that name in whatever namespace, which is how a stylesheet reaches into a
                // document whose namespace it would otherwise have to declare a prefix for.
                m_index++;
                return new LocalNameNodeTest(token.Text);
            }

            if (token.Prefix.Length == 0
                && Peek(1).Kind == XPathTokenKind.LeftParen
                && s_nodeTypes.Contains(token.Text))
            {
                return ParseNodeTypeTest(token);
            }

            if (token.Prefix.Length == 0
                && Peek(1).Kind == XPathTokenKind.LeftParen
                && s_kindTests.Contains(token.Text))
            {
                return ParseKindTest(token);
            }

            m_index++;

            // An unprefixed name is in no namespace unless the host said otherwise: the document's default
            // namespace deliberately does not apply, and XSLT's xpath-default-namespace is how a stylesheet
            // asks for one. It reaches an element name and not an attribute's, the attribute axis having no
            // default namespace of its own.
            string namespaceUri = token.Prefix.Length != 0
                ? ResolvePrefixOrThrow(token)
                : axis.PrincipalNodeKind() == NodeKind.Element ? m_context.DefaultElementNamespace : string.Empty;

            int slot = m_context.Names.GetSlot(namespaceUri, token.Text);
            string display = token.Prefix.Length == 0 ? token.Text : $"{token.Prefix}:{token.Text}";
            return new NameNodeTest(slot, display);
        }

        private NodeTest ParseNodeTypeTest(XPathToken token)
        {
            m_index += 2;

            // The target may be written as a name as readily as as a string: processing-instruction(go) and
            // processing-instruction('go') are the same test, and the grammar has said so since XPath 1.0.
            // A name is the form anyone actually writes, and it was the form this refused.
            if (token.Text == "processing-instruction"
                && Current.Kind is XPathTokenKind.StringLiteral or XPathTokenKind.Name
                && Current.Prefix.Length == 0)
            {
                string target = Current.Text;
                m_index++;
                Expect(XPathTokenKind.RightParen);
                return new ProcessingInstructionNodeTest(target);
            }

            Expect(XPathTokenKind.RightParen);

            return token.Text switch
            {
                "node" => NodeTest.AnyNode,
                "text" => NodeTest.AnyText,
                "comment" => NodeTest.AnyComment,
                "namespace-node" => new KindNodeTest(NodeKind.Namespace, null, null, null, "namespace-node()"),
                _ => new ProcessingInstructionNodeTest(null),
            };
        }

        /// <summary>
        /// Parses <c>element(…)</c>, <c>attribute(…)</c>, <c>document-node(…)</c> and the two schema forms.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These are the kind tests XPath 2.0 adds, and unlike a name test they say which kind of node they
        /// want: <c>child::attribute()</c> selects nothing, where <c>child::*</c> selects every element.
        /// </para>
        /// <para>
        /// The two schema forms parse and then refuse. They name a declaration in a schema, and this engine
        /// is not schema-aware, so there is never one to find — <c>XPST0008</c>, which is what a name that
        /// is not declared gets, and not a claim that the syntax is wrong.
        /// </para>
        /// </remarks>
        /// <param name="token">The name that opened the test, already known to be one of the five.</param>
        private KindNodeTest ParseKindTest(XPathToken token)
        {
            m_index += 2;

            switch (token.Text)
            {
                case "schema-element":
                case "schema-attribute":
                {
                    XPathToken named = Current;
                    (string uri, string local) = ParseKindTestName(token, wildcardAllowed: false)
                        ?? throw Error(token, $"{token.Text}() names a declaration, so it needs one.");

                    Expect(XPathTokenKind.RightParen);

                    // The declaration is looked for among the schema components in scope, which a
                    // processor that is not schema-aware has none of: XPST0008, which is what a name that
                    // is not declared gets, and not a claim that the syntax is wrong.
                    bool element = token.Text == "schema-element";
                    XdmSchemaDeclaration? declaration = m_context is ISchemaTypeProvider provider
                        ? element ? provider.ResolveElementDeclaration(uri, local) : provider.ResolveAttributeDeclaration(uri, local)
                        : null;

                    if (declaration is null)
                    {
                        throw Error(
                            token,
                            XsltErrorCode.XPST0008,
                            $"There is no declaration of '{(uri.Length == 0 ? local : $"{{{uri}}}{local}")}'"
                            + (m_context is ISchemaTypeProvider
                                ? " among the schemas in scope"
                                : ": this engine is not schema-aware, so it has none to look in"));
                    }

                    return new KindNodeTest(
                        element ? NodeKind.Element : NodeKind.Attribute, uri, local, null,
                        token.Text + "(" + TypeNameWritten(named) + ")")
                    {
                        NamesAType = true,
                        TypeName = declaration.Type.Written,
                        Declaration = declaration,
                        AdmitsNilled = declaration.Nillable,
                    };
                }

                case "document-node":
                {
                    KindNodeTest? content = null;

                    if (Current.Kind != XPathTokenKind.RightParen)
                    {
                        // Only an element test may go inside: a document node is asked about through the
                        // element it holds, and there is nothing else it could be asked about.
                        if (Current.Kind != XPathTokenKind.Name
                            || Current.Prefix.Length != 0
                            || Current.Text is not ("element" or "schema-element")
                            || Peek(1).Kind != XPathTokenKind.LeftParen)
                        {
                            throw Error(
                                Current,
                                "document-node() takes an element test, or nothing at all.");
                        }

                        content = ParseKindTest(Current);
                    }

                    Expect(XPathTokenKind.RightParen);
                    return new KindNodeTest(
                        NodeKind.Root, null, null, content,
                        content is null ? "document-node()" : $"document-node({content})");
                }

                default:
                {
                    NodeKind kind = token.Text == "element" ? NodeKind.Element : NodeKind.Attribute;
                    (string Uri, string Local)? name = ParseKindTestName(token, wildcardAllowed: true);

                    // A type annotation after the name, which a node validated against a schema carries
                    // and a node nothing validated has as xs:untyped or xs:untypedAtomic. That it was
                    // written counts too: a test naming both halves is the most specific kind test there
                    // is, and as a pattern it takes the priority that goes with saying more (XSLT 3.0 §6.5).
                    string? annotation = null;
                    XdmSchemaType? annotationType = null;
                    bool nillable = true;

                    if (Current.Kind == XPathTokenKind.Comma)
                    {
                        m_index++;
                        annotation = ParseTypeAnnotation(out annotationType, out nillable);
                    }

                    Expect(XPathTokenKind.RightParen);

                    // The annotation belongs in the written form, which is how two kind tests are told
                    // apart where they are compared as written: element(e) and element(e, xs:anyType) are
                    // not the same test, the first being nillable and the second not.
                    string inside = name is null
                        ? annotation is null ? string.Empty : "*"
                        : name.Value.Local;

                    string written = annotation is null
                        ? token.Text + "(" + inside + ")"
                        : token.Text + "(" + inside + ", " + annotation + (nillable ? "?" : string.Empty) + ")";

                    return new KindNodeTest(kind, name?.Uri, name?.Local, null, written)
                    {
                        NamesAType = annotation is not null,
                        TypeName = annotation,
                        SchemaType = annotationType,
                        AdmitsNilled = nillable,
                    };
                }
            }
        }

        /// <summary>
        /// Parses the name inside a kind test, or returns null where the test named none.
        /// </summary>
        /// <param name="token">The test's own name, for the message.</param>
        /// <param name="wildcardAllowed">Whether <c>*</c> stands for "any name" here.</param>
        private (string Uri, string Local)? ParseKindTestName(XPathToken token, bool wildcardAllowed)
        {
            if (Current.Kind == XPathTokenKind.RightParen)
            {
                return null;
            }

            if (Current.Kind == XPathTokenKind.Star || Current.Text == "*")
            {
                if (!wildcardAllowed)
                {
                    throw Error(Current, $"{token.Text}() names one declaration, so '*' will not do.");
                }

                m_index++;
                return null;
            }

            if (Current.Kind != XPathTokenKind.Name)
            {
                throw Error(Current, $"{token.Text}() takes a name.");
            }

            XPathToken name = Current;
            m_index++;

            // An unprefixed element name inside a kind test is in the default element namespace, as a
            // name test's is (XPath 3.1 §2.5.5.3), and an unprefixed attribute name is in none.
            if (name.NamespaceUri is string braced)
            {
                return (braced, name.Text);
            }

            if (name.Prefix.Length != 0)
            {
                return (ResolvePrefixOrThrow(name), name.Text);
            }

            return (
                token.Text is "element" or "schema-element" ? m_context.DefaultElementNamespace : string.Empty,
                name.Text);
        }

        /// <summary>Reads the type name a kind test may carry, and checks that it is one.</summary>
        /// <summary>
        /// Reads the type an element or attribute test names, and answers with its local part.
        /// </summary>
        /// <remarks>
        /// The two names of the types nothing carries — <c>xs:anyType</c> and <c>xs:anySimpleType</c> — are
        /// admitted here alongside the ones a value can be. They name nothing constructible and so are not
        /// in the type table, but an unvalidated element is annotated <c>xs:untyped</c> and an unvalidated
        /// attribute <c>xs:untypedAtomic</c>, and a test may name a type either of those derives from.
        /// </remarks>
        private string ParseTypeAnnotation(out XdmSchemaType? type, out bool nillable)
        {
            if (Current.Kind != XPathTokenKind.Name)
            {
                throw Error(Current, "A type name was expected after the comma.");
            }

            XPathToken name = Current;
            m_index++;

            // Trailing '?', which says the element may be nilled.
            nillable = false;

            if (Current.Kind == XPathTokenKind.Question)
            {
                m_index++;
                nillable = true;
            }

            if (m_context.ResolvePrefix(name.Prefix) == XdmType.SchemaNamespace
                && (XdmType.TryGet(name.Text, out XdmType.BuiltInType _)
                    || name.Text is "anyType" or "anySimpleType")
                && XdmSchemaType.BuiltInNamed(name.Text) is XdmSchemaType builtIn)
            {
                type = builtIn;
                return name.Text;
            }

            // A type from a schema, where the context has schema components; the written form keeps
            // the name as the stylesheet spelt it.
            if (TypeNamespaceOf(name) is string uri
                && m_context is ISchemaTypeProvider provider
                && provider.ResolveSchemaType(uri, name.Text) is XdmSchemaType found)
            {
                type = found;
                return TypeNameWritten(name);
            }

            throw Error(
                name, XsltErrorCode.XPST0051, $"'{name.Text}' is not a type this engine knows");
        }

        /// <summary>A type name as it was written, prefix and all, for the written form of a type.</summary>
        private static string TypeNameWritten(XPathToken token)
        {
            return token.NamespaceUri is string braced
                ? "Q{" + braced + "}" + token.Text
                : token.Prefix.Length == 0 ? token.Text : token.Prefix + ":" + token.Text;
        }

        private Expr[] ParsePredicates()
        {
            if (Current.Kind != XPathTokenKind.LeftBracket)
            {
                return Array.Empty<Expr>();
            }

            List<Expr> predicates = new List<Expr>();
            while (Current.Kind == XPathTokenKind.LeftBracket)
            {
                predicates.Add(ParsePredicate());
            }

            return predicates.ToArray();
        }

        private Expr ParsePredicate()
        {
            Expect(XPathTokenKind.LeftBracket);
            Expr predicate = ParseExpression();
            Expect(XPathTokenKind.RightBracket);
            return predicate;
        }

        private string ResolvePrefixOrThrow(XPathToken token)
        {
            // A name written as 'Q{uri}local' carries its namespace and needs no lookup.
            if (token.NamespaceUri is string braced)
            {
                return braced;
            }

            string? uri = NamespaceOf(token);
            if (uri is null)
            {
                throw Error(token, XsltErrorCode.XPST0081, $"Namespace prefix '{token.Prefix}' is not bound.");
            }

            return uri;
        }

        /// <summary>The namespace a name is in, or <see langword="null"/> where its prefix is not bound.</summary>
        private string? NamespaceOf(XPathToken token)
        {
            return token.NamespaceUri ?? m_context.ResolvePrefix(token.Prefix);
        }

        /// <summary>
        /// The namespace a type name is in, which for an unprefixed one is the default element/type
        /// namespace rather than whatever <c>xmlns</c> happens to say.
        /// </summary>
        private string? TypeNamespaceOf(XPathToken token)
        {
            if (token.NamespaceUri is string braced)
            {
                return braced;
            }

            return token.Prefix.Length == 0
                ? m_context.DefaultElementNamespace
                : m_context.ResolvePrefix(token.Prefix);
        }

        private void Expect(XPathTokenKind kind)
        {
            if (Current.Kind != kind)
            {
                throw Error(Current, $"Expected {Describe(kind)} but found {Describe(Current.Kind)}.");
            }

            if (kind != XPathTokenKind.End)
            {
                m_index++;
            }
        }

        /// <summary>
        /// Reports a syntax error, which is what nearly every failure to parse is.
        /// </summary>
        /// <remarks>
        /// <c>XPST0003</c> is the code the specification gives to an expression that does not fit the grammar.
        /// The few parse-time errors that are something else — a name that is not bound, a function that does
        /// not exist — say so through the overload that names their own code.
        /// </remarks>
        private XsltException Error(XPathToken token, string message)
        {
            return Error(token, XsltErrorCode.XPST0003, message);
        }

        private XsltException Error(XPathToken token, XsltErrorCode code, string message)
        {
            return new XsltException(code, $"{message} (at offset {token.Position} in \"{m_source}\")");
        }

        /// <summary>
        /// Adds the position to an error raised while building an expression, without losing its code.
        /// </summary>
        /// <remarks>
        /// Wrapping the message would otherwise discard the error code the specification names, which is
        /// exactly what a conformance suite checks. The code survives; only the text gains the offset.
        /// </remarks>
        private XsltException Locate(XPathToken token, XsltException error)
        {
            string message = $"{error.Message} (at offset {token.Position} in \"{m_source}\")";

            return error.Code is null || !Enum.TryParse(error.Code, out XsltErrorCode code)
                ? new XsltException(message)
                : new XsltException(code, message);
        }

        private static string Describe(XPathTokenKind kind)
        {
            return kind switch
            {
                XPathTokenKind.End => "end of expression",
                XPathTokenKind.LeftParen => "'('",
                XPathTokenKind.RightParen => "')'",
                XPathTokenKind.LeftBracket => "'['",
                XPathTokenKind.RightBracket => "']'",
                XPathTokenKind.Name => "a name",
                XPathTokenKind.Number => "a number",
                XPathTokenKind.StringLiteral => "a string literal",
                _ => kind.ToString(),
            };
        }
    }
}
