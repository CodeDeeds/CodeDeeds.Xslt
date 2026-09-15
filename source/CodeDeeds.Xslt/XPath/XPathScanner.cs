using System.Globalization;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The lexical classes produced by <see cref="XPathScanner"/>.</summary>
    internal enum XPathTokenKind : byte
    {
        /// <summary>End of input.</summary>
        End,

        /// <summary>A qualified name, possibly with a <c>*</c> local part.</summary>
        Name,

        /// <summary>A numeric literal.</summary>
        Number,

        /// <summary>A quoted string literal.</summary>
        StringLiteral,

        /// <summary>A variable reference, written <c>$name</c>.</summary>
        Variable,

        /// <summary><c>/</c></summary>
        Slash,

        /// <summary><c>//</c></summary>
        DoubleSlash,

        /// <summary><c>(</c></summary>
        LeftParen,

        /// <summary><c>)</c></summary>
        RightParen,

        /// <summary><c>[</c></summary>
        LeftBracket,

        /// <summary><c>]</c></summary>
        RightBracket,

        /// <summary><c>.</c></summary>
        Dot,

        /// <summary><c>..</c></summary>
        DoubleDot,

        /// <summary><c>@</c></summary>
        At,

        /// <summary><c>,</c></summary>
        Comma,

        /// <summary><c>::</c></summary>
        DoubleColon,

        /// <summary><c>*</c> in a position where it is a name test, not multiplication.</summary>
        Star,

        /// <summary><c>*</c> in a position where it is the multiplication operator.</summary>
        Multiply,

        /// <summary><c>+</c></summary>
        Plus,

        /// <summary><c>-</c></summary>
        Minus,

        /// <summary><c>=</c></summary>
        Equal,

        /// <summary><c>!=</c></summary>
        NotEqual,

        /// <summary><c>&lt;</c></summary>
        LessThan,

        /// <summary><c>&lt;=</c></summary>
        LessThanOrEqual,

        /// <summary><c>&gt;</c></summary>
        GreaterThan,

        /// <summary><c>&gt;=</c></summary>
        GreaterThanOrEqual,

        /// <summary><c>|</c></summary>
        Pipe,

        /// <summary><c>||</c>, the string concatenation operator XPath 3.0 adds.</summary>
        Concat,

        /// <summary>The <c>and</c> operator.</summary>
        And,

        /// <summary>The <c>or</c> operator.</summary>
        Or,

        /// <summary>The <c>mod</c> operator.</summary>
        Mod,

        /// <summary>The <c>div</c> operator.</summary>
        Div,

        /// <summary>The XPath 2.0 <c>to</c> range operator.</summary>
        To,

        /// <summary>The XPath 2.0 <c>idiv</c> operator, which divides and truncates to an integer.</summary>
        IDiv,

        /// <summary>
        /// One of the XPath 2.0 value comparison operators — <c>eq</c>, <c>ne</c>, <c>lt</c>, <c>le</c>,
        /// <c>gt</c> or <c>ge</c> — which compare single values rather than quantifying over sequences.
        /// </summary>
        ValueComparison,

        /// <summary>The XPath 2.0 <c>&lt;&lt;</c> operator, asking whether one node precedes another.</summary>
        Precedes,

        /// <summary>The XPath 2.0 <c>&gt;&gt;</c> operator, asking whether one node follows another.</summary>
        Follows,

        /// <summary>The XPath 2.0 <c>is</c> operator, asking whether two references are the same node.</summary>
        Is,

        /// <summary>The XPath 2.0 <c>intersect</c> operator.</summary>
        Intersect,

        /// <summary>The XPath 2.0 <c>except</c> operator.</summary>
        Except,

        /// <summary>The XPath 2.0 <c>union</c> operator, a spelling of <c>|</c>.</summary>
        Union,

        /// <summary>
        /// <c>?</c>, the occurrence indicator meaning "or nothing". XPath 1.0 has no use for it.
        /// </summary>
        Question,

        /// <summary><c>{</c>, which opens a <c>map</c> or <c>array</c> constructor in XPath 3.1.</summary>
        LeftBrace,

        /// <summary><c>}</c>, which closes one.</summary>
        RightBrace,

        /// <summary>
        /// <c>:</c> standing alone, which separates a key from its value in a map constructor.
        /// </summary>
        /// <remarks>
        /// A colon inside a qualified name never reaches here, nor does the <c>::</c> of an axis: both are
        /// taken while the name is scanned.
        /// </remarks>
        Colon,

        /// <summary>
        /// <c>#</c>, which separates a function's name from its arity in <c>fn:concat#3</c>.
        /// </summary>
        Hash,

        /// <summary>
        /// <c>=&gt;</c>, the XPath 3.1 arrow, which passes what is on its left as the first argument of the
        /// call on its right.
        /// </summary>
        Arrow,

        /// <summary><c>:=</c>, which binds a variable in a <c>let</c>.</summary>
        Assign,

        /// <summary>
        /// <c>!</c>, the XPath 3.0 simple map operator, which applies what is on its right to each item on
        /// its left.
        /// </summary>
        Bang,
    }

    /// <summary>One lexical token.</summary>
    internal readonly struct XPathToken
    {
        /// <summary>Initializes a token.</summary>
        public XPathToken(XPathTokenKind kind, int position, string prefix = "", string text = "", double number = 0)
        {
            Kind = kind;
            Position = position;
            Prefix = prefix;
            Text = text;
            Number = number;
        }

        /// <summary>The token's lexical class.</summary>
        public XPathTokenKind Kind { get; }

        /// <summary>The offset in the source expression at which the token starts.</summary>
        public int Position { get; }

        /// <summary>The prefix of a <see cref="XPathTokenKind.Name"/>, or an empty string.</summary>
        public string Prefix { get; }

        /// <summary>The local name, string value, or variable name, depending on the kind.</summary>
        public string Text { get; }

        /// <summary>The value of a <see cref="XPathTokenKind.Number"/>.</summary>
        public double Number { get; }

        /// <summary>
        /// The namespace of a name written as <c>Q{uri}local</c>, which needs no prefix to resolve.
        /// </summary>
        /// <remarks>
        /// <see cref="Prefix"/> carries the same URI for such a name, so that every test asking whether a
        /// name has a namespace still answers yes; what this adds is that the answer needs no lookup.
        /// </remarks>
        public string? NamespaceUri { get; init; }
    }

    /// <summary>
    /// Turns an XPath expression into a token list.
    /// </summary>
    /// <remarks>
    /// XPath's grammar is not context-free at the lexical level. Whether <c>*</c> means multiplication or a
    /// wildcard name test, and whether <c>div</c> is an operator or an element name, both depend on what came
    /// immediately before. The rule from the specification is that after a token which can end an operand —
    /// a name, a number, a literal, <c>)</c>, <c>]</c>, <c>.</c>, <c>..</c>, <c>*</c> or a variable — the
    /// scanner is in "operator position", and <c>*</c>, <c>and</c>, <c>or</c>, <c>mod</c> and <c>div</c> are
    /// read as operators. Anywhere else they are read as names. Tokenizing the whole expression up front makes
    /// that rule a simple check against the previous token.
    /// </remarks>
    internal static class XPathScanner
    {
        /// <summary>
        /// Scans an expression into tokens, terminated by <see cref="XPathTokenKind.End"/>.
        /// </summary>
        /// <param name="expression">The expression text.</param>
        /// <returns>The token list.</returns>
        /// <exception cref="XsltException">The expression contains a character that cannot start a token.</exception>
        public static List<XPathToken> Scan(string expression)
        {
            List<XPathToken> tokens = new List<XPathToken>();
            int index = 0;

            while (true)
            {
                while (index < expression.Length)
                {
                    if (IsWhitespace(expression[index]))
                    {
                        index++;
                        continue;
                    }

                    // A comment is whitespace as far as anything downstream is concerned, and it may sit
                    // wherever whitespace may — including between a name and the parenthesis of its call.
                    if (expression[index] == '(' && index + 1 < expression.Length
                        && expression[index + 1] == ':')
                    {
                        index = SkipComment(expression, index);
                        continue;
                    }

                    break;
                }

                if (index >= expression.Length)
                {
                    tokens.Add(new XPathToken(XPathTokenKind.End, index));
                    return tokens;
                }

                int start = index;
                char c = expression[index];

                switch (c)
                {
                    case '(':
                        tokens.Add(new XPathToken(XPathTokenKind.LeftParen, start));
                        index++;
                        continue;

                    case ')':
                        tokens.Add(new XPathToken(XPathTokenKind.RightParen, start));
                        index++;
                        continue;

                    case '[':
                        tokens.Add(new XPathToken(XPathTokenKind.LeftBracket, start));
                        index++;
                        continue;

                    case ']':
                        tokens.Add(new XPathToken(XPathTokenKind.RightBracket, start));
                        index++;
                        continue;

                    case '@':
                        tokens.Add(new XPathToken(XPathTokenKind.At, start));
                        index++;
                        continue;

                    case ',':
                        tokens.Add(new XPathToken(XPathTokenKind.Comma, start));
                        index++;
                        continue;

                    case '+':
                        tokens.Add(new XPathToken(XPathTokenKind.Plus, start));
                        index++;
                        continue;

                    case '-':
                        tokens.Add(new XPathToken(XPathTokenKind.Minus, start));
                        index++;
                        continue;

                    case '=':
                        // "=>" is the arrow, and has to be recognised before '=' or it would scan as an
                        // equality against a greater-than with nothing between them.
                        if (index + 1 < expression.Length && expression[index + 1] == '>')
                        {
                            tokens.Add(new XPathToken(XPathTokenKind.Arrow, start));
                            index += 2;
                            continue;
                        }

                        tokens.Add(new XPathToken(XPathTokenKind.Equal, start));
                        index++;
                        continue;

                    case '#':
                        tokens.Add(new XPathToken(XPathTokenKind.Hash, start));
                        index++;
                        continue;

                    case '|':
                        // No ambiguity to weigh: a union operand cannot be empty, so two pipes in a row
                        // were never anything else.
                        if (index + 1 < expression.Length && expression[index + 1] == '|')
                        {
                            tokens.Add(new XPathToken(XPathTokenKind.Concat, start));
                            index += 2;
                            continue;
                        }

                        tokens.Add(new XPathToken(XPathTokenKind.Pipe, start));
                        index++;
                        continue;

                    case '?':
                        tokens.Add(new XPathToken(XPathTokenKind.Question, start));
                        index++;
                        continue;

                    case '*':
                        // "*:local" is a name test with the prefix wildcarded, so it is one token. Only where
                        // a name may start: after an operand, '*' is multiplication and a colon could not
                        // follow it.
                        if (!InOperatorPosition(tokens)
                            && index + 2 < expression.Length
                            && expression[index + 1] == ':'
                            && IsNameStart(expression[index + 2]))
                        {
                            index += 2;
                            (_, string wildcarded) = ScanQualifiedName(expression, ref index, allowWildcard: false);
                            tokens.Add(new XPathToken(XPathTokenKind.Name, start, "*", wildcarded));
                            continue;
                        }

                        tokens.Add(new XPathToken(
                            InOperatorPosition(tokens) ? XPathTokenKind.Multiply : XPathTokenKind.Star, start));
                        index++;
                        continue;

                    case '/':
                        if (index + 1 < expression.Length && expression[index + 1] == '/')
                        {
                            tokens.Add(new XPathToken(XPathTokenKind.DoubleSlash, start));
                            index += 2;
                        }
                        else
                        {
                            tokens.Add(new XPathToken(XPathTokenKind.Slash, start));
                            index++;
                        }

                        continue;

                    case '{':
                        tokens.Add(new XPathToken(XPathTokenKind.LeftBrace, start));
                        index++;
                        continue;

                    case '}':
                        tokens.Add(new XPathToken(XPathTokenKind.RightBrace, start));
                        index++;
                        continue;

                    case ':':
                        if (index + 1 < expression.Length && expression[index + 1] == ':')
                        {
                            tokens.Add(new XPathToken(XPathTokenKind.DoubleColon, start));
                            index += 2;
                            continue;
                        }

                        if (index + 1 < expression.Length && expression[index + 1] == '=')
                        {
                            tokens.Add(new XPathToken(XPathTokenKind.Assign, start));
                            index += 2;
                            continue;
                        }

                        // Standing alone it separates a map entry's key from its value. Anywhere else the
                        // parser refuses it, which says where the trouble is more usefully than the scanner
                        // could — it knows only that a colon was not expected, not what was.
                        tokens.Add(new XPathToken(XPathTokenKind.Colon, start));
                        index++;
                        continue;

                    case '!':
                        if (index + 1 < expression.Length && expression[index + 1] == '=')
                        {
                            tokens.Add(new XPathToken(XPathTokenKind.NotEqual, start));
                            index += 2;
                            continue;
                        }

                        // Standing alone it is the XPath 3.0 simple map operator. Below 3.0 the parser
                        // refuses it, which is where the version is known.
                        tokens.Add(new XPathToken(XPathTokenKind.Bang, start));
                        index++;
                        continue;

                    case '<':
                    case '>':
                    {
                        char next = index + 1 < expression.Length ? expression[index + 1] : '\0';

                        // "<<" and ">>" are the XPath 2.0 node comparisons, and must be recognised before
                        // the single-character operators or they would scan as two of them.
                        if (next == c)
                        {
                            tokens.Add(new XPathToken(
                                c == '<' ? XPathTokenKind.Precedes : XPathTokenKind.Follows, start));

                            index += 2;
                            continue;
                        }

                        bool orEqual = next == '=';
                        XPathTokenKind kind = c == '<'
                            ? orEqual ? XPathTokenKind.LessThanOrEqual : XPathTokenKind.LessThan
                            : orEqual ? XPathTokenKind.GreaterThanOrEqual : XPathTokenKind.GreaterThan;

                        tokens.Add(new XPathToken(kind, start));
                        index += orEqual ? 2 : 1;
                        continue;
                    }

                    case '\'':
                    case '"':
                    {
                        tokens.Add(new XPathToken(
                            XPathTokenKind.StringLiteral, start, text: ScanString(expression, ref index, c)));

                        continue;
                    }

                    case '$':
                    {
                        index++;

                        // A variable may be named 'Q{uri}x' as readily as an element may.
                        if (index < expression.Length && expression[index] == 'Q'
                            && index + 1 < expression.Length && expression[index + 1] == '{')
                        {
                            XPathToken braced = ScanBracedName(expression, ref index);
                            tokens.Add(new XPathToken(
                                XPathTokenKind.Variable, start, braced.Prefix, braced.Text)
                            {
                                NamespaceUri = braced.NamespaceUri,
                            });

                            continue;
                        }

                        // A name and not merely name characters: $2 is no more a variable than 2 is
                        // an element, a digit being unable to start an XML name. The scan below reads
                        // name characters and would take it.
                        if (index >= expression.Length || !IsNameStart(expression[index]))
                        {
                            throw Error(expression, start, "'$' must be followed by a variable name.");
                        }

                        (string prefix, string local) = ScanQualifiedName(expression, ref index, allowWildcard: false);
                        if (local.Length == 0)
                        {
                            throw Error(expression, start, "'$' must be followed by a variable name.");
                        }

                        tokens.Add(new XPathToken(XPathTokenKind.Variable, start, prefix, local));
                        continue;
                    }
                }

                if (c == '.' && !(index + 1 < expression.Length && IsDigit(expression[index + 1])))
                {
                    if (index + 1 < expression.Length && expression[index + 1] == '.')
                    {
                        tokens.Add(new XPathToken(XPathTokenKind.DoubleDot, start));
                        index += 2;
                    }
                    else
                    {
                        tokens.Add(new XPathToken(XPathTokenKind.Dot, start));
                        index++;
                    }

                    continue;
                }

                if (IsDigit(c) || c == '.')
                {
                    tokens.Add(ScanNumber(expression, ref index));
                    continue;
                }

                // 'Q{uri}local' names a namespace outright rather than through a prefix, which is what lets
                // an expression reach a namespace nothing declared a prefix for.
                if (c == 'Q' && index + 1 < expression.Length && expression[index + 1] == '{')
                {
                    tokens.Add(ScanBracedName(expression, ref index));
                    continue;
                }

                if (IsNameStart(c))
                {
                    (string prefix, string local) = ScanQualifiedName(expression, ref index, allowWildcard: true);

                    if (prefix.Length == 0 && InOperatorPosition(tokens))
                    {
                        XPathTokenKind? operatorKind = local switch
                        {
                            "and" => XPathTokenKind.And,
                            "or" => XPathTokenKind.Or,
                            "mod" => XPathTokenKind.Mod,
                            "div" => XPathTokenKind.Div,

                            // XPath 2.0 additions. They are ordinary names elsewhere — an element may well be
                            // called "to" — which is why this only applies in operator position.
                            "to" => XPathTokenKind.To,
                            "idiv" => XPathTokenKind.IDiv,
                            "eq" or "ne" or "lt" or "le" or "gt" or "ge" => XPathTokenKind.ValueComparison,
                            "is" => XPathTokenKind.Is,
                            "intersect" => XPathTokenKind.Intersect,
                            "except" => XPathTokenKind.Except,
                            "union" => XPathTokenKind.Union,
                            _ => null,
                        };

                        if (operatorKind.HasValue)
                        {
                            tokens.Add(operatorKind.Value == XPathTokenKind.ValueComparison
                                ? new XPathToken(XPathTokenKind.ValueComparison, start, string.Empty, local)
                                : new XPathToken(operatorKind.Value, start));
                            continue;
                        }
                    }

                    tokens.Add(new XPathToken(XPathTokenKind.Name, start, prefix, local));
                    continue;
                }

                throw Error(expression, start, $"Unexpected character '{c}'.");
            }
        }

        /// <summary>
        /// Skips a comment, <c>(: … :)</c>, and returns the index just past it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XPath 2.0's only comment, and it nests: <c>(: outer (: inner :) still outer :)</c> is one comment,
        /// which is what lets a reader comment out a stretch of expression that already had one in it.
        /// Nesting is why this counts depth rather than searching for the first <c>:)</c>.
        /// </para>
        /// <para>
        /// Not gated on the version, though XPath 1.0 has no comments. There is no 1.0 expression in which
        /// <c>(:</c> means anything — a colon could not follow a parenthesis — so reading it this way takes
        /// nothing away from 1.0, the same argument as for the doubled-quote escape.
        /// </para>
        /// </remarks>
        /// <param name="expression">The expression text.</param>
        /// <param name="start">The index of the opening parenthesis.</param>
        /// <exception cref="XsltException">The comment is never closed.</exception>
        private static int SkipComment(string expression, int start)
        {
            int depth = 0;
            int index = start;

            while (index + 1 < expression.Length)
            {
                if (expression[index] == '(' && expression[index + 1] == ':')
                {
                    depth++;
                    index += 2;
                    continue;
                }

                if (expression[index] == ':' && expression[index + 1] == ')')
                {
                    depth--;
                    index += 2;

                    if (depth == 0)
                    {
                        return index;
                    }

                    continue;
                }

                index++;
            }

            throw Error(expression, start, "This comment is never closed. A '(:' needs a ':)'.");
        }

        /// <summary>
        /// Returns whether the scanner is positioned where an operator may appear, which is immediately after
        /// any token that can end an operand.
        /// </summary>
        private static bool InOperatorPosition(List<XPathToken> tokens)
        {
            if (tokens.Count == 0)
            {
                return false;
            }

            XPathToken last = tokens[^1];

            // 'for $x in * return *': a word that can only introduce an operand is not an operand itself,
            // so a star after it is a wildcard — provided the word is standing where the keyword stands,
            // after an operand of its own ('$x in', '$y return'). At the start, or after an operator, it is
            // an element that happens to be called 'in', and a star after that multiplies.
            if (last.Kind == XPathTokenKind.Name
                && last.Prefix.Length == 0
                && last.NamespaceUri is null
                && last.Text is "in" or "return" or "satisfies" or "then" or "else"
                && tokens.Count >= 2
                && EndsOperand(tokens[^2]))
            {
                return false;
            }

            return EndsOperand(last);
        }

        /// <summary>Whether a token is one an operand can end with, so that an operator may follow it.</summary>
        private static bool EndsOperand(XPathToken token)
        {
            return token.Kind switch
            {
                XPathTokenKind.Name => true,
                XPathTokenKind.Number => true,
                XPathTokenKind.StringLiteral => true,
                XPathTokenKind.Variable => true,
                XPathTokenKind.RightParen => true,
                XPathTokenKind.RightBracket => true,
                XPathTokenKind.RightBrace => true,
                XPathTokenKind.Star => true,
                XPathTokenKind.Dot => true,
                XPathTokenKind.DoubleDot => true,
                _ => false,
            };
        }

        /// <summary>
        /// Reads a string literal, in which the delimiting quote is written twice to mean itself.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XPath 2.0's only escape: <c>'don''t'</c> is <c>don't</c>, and <c>"say ""so"""</c> is
        /// <c>say "so"</c>. Without it the shorter of the two quotes has to be reached for, and a string
        /// holding both cannot be written at all.
        /// </para>
        /// <para>
        /// XPath 1.0 has no escape, where the same text is two literals side by side and no 1.0 expression —
        /// so reading it this way takes nothing away from 1.0, the same argument as for the exponent.
        /// </para>
        /// </remarks>
        private static string ScanString(string expression, ref int index, char quote)
        {
            int start = index;
            int from = index + 1;
            System.Text.StringBuilder? builder = null;

            while (true)
            {
                int closing = expression.IndexOf(quote, from);
                if (closing < 0)
                {
                    throw Error(expression, start, "Unterminated string literal.");
                }

                // A doubled quote is one quote in the value, and the literal carries on past it.
                if (closing + 1 < expression.Length && expression[closing + 1] == quote)
                {
                    builder ??= new System.Text.StringBuilder();
                    builder.Append(expression, from, closing - from).Append(quote);
                    from = closing + 2;
                    continue;
                }

                index = closing + 1;

                if (builder is null)
                {
                    return expression[(start + 1)..closing];
                }

                return builder.Append(expression, from, closing - from).ToString();
            }
        }

        private static XPathToken ScanNumber(string expression, ref int index)
        {
            int start = index;
            while (index < expression.Length && IsDigit(expression[index]))
            {
                index++;
            }

            if (index < expression.Length && expression[index] == '.')
            {
                index++;
                while (index < expression.Length && IsDigit(expression[index]))
                {
                    index++;
                }
            }

            ScanExponent(expression, ref index);

            // A number and a name that follows it have to be separated, which is what makes '10div 3'
            // a syntax error rather than ten divided by three. The grammar says so outright, and it
            // has to: without the rule there is no telling '10div' from a number named 10div.
            if (index < expression.Length && IsNameStart(expression[index]))
            {
                throw Error(
                    expression,
                    index,
                    $"A number and the name after it need whitespace between them, and '" +
                        expression[start..index] + "' runs straight into one");
            }

            string lexical = expression[start..index];
            double value = double.Parse(lexical, NumberStyles.Float, CultureInfo.InvariantCulture);

            // The lexical form is kept because XPath 2.0 types a literal by how it was written: 3 is an
            // xs:integer, 3.0 an xs:decimal and 3e0 an xs:double, and they behave differently under division.
            return new XPathToken(XPathTokenKind.Number, start, text: lexical, number: value);
        }

        /// <summary>
        /// Takes in an exponent, if what follows is one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The exponent is only part of the number when a digit follows it, which is what keeps this from
        /// swallowing a name: in <c>1e</c> the <c>e</c> begins something else, and only <c>1e5</c> is a
        /// number. The sign, if written, belongs to the exponent and not to a subtraction.
        /// </para>
        /// <para>
        /// XPath 1.0 has no exponent in its number syntax at all, and this does not ask which version is in
        /// force. Nothing is lost by that: the text an exponent claims here would otherwise scan as a number
        /// followed by a name, which is not a 1.0 expression either.
        /// </para>
        /// </remarks>
        private static void ScanExponent(string expression, ref int index)
        {
            if (index >= expression.Length || (expression[index] != 'e' && expression[index] != 'E'))
            {
                return;
            }

            int after = index + 1;

            if (after < expression.Length && (expression[after] == '+' || expression[after] == '-'))
            {
                after++;
            }

            if (after >= expression.Length || !IsDigit(expression[after]))
            {
                return;
            }

            index = after;
            while (index < expression.Length && IsDigit(expression[index]))
            {
                index++;
            }
        }

        /// <summary>
        /// Scans a qualified name. A <c>:</c> is only treated as a prefix separator when it is not part of the
        /// <c>::</c> axis separator, so <c>child::x</c> scans as the name <c>child</c> followed by <c>::</c>.
        /// </summary>
        private static (string Prefix, string Local) ScanQualifiedName(
            string expression,
            ref int index,
            bool allowWildcard)
        {
            string first = ScanNcName(expression, ref index);

            // A colon separates a prefix only when a name follows it. That rules out the '::' of an axis,
            // since a colon does not start a name, and it leaves the colon of a map entry alone: in
            // "map { a: 1 }" the key is the element a, and taking the colon would make the name 'a:' with
            // nothing after it.
            bool hasPrefix = index + 1 < expression.Length
                && expression[index] == ':'
                && (IsNameStart(expression[index + 1])
                    || (allowWildcard && expression[index + 1] == '*'));

            if (!hasPrefix)
            {
                return (string.Empty, first);
            }

            index++;

            if (allowWildcard && index < expression.Length && expression[index] == '*')
            {
                index++;
                return (first, "*");
            }

            return (first, ScanNcName(expression, ref index));
        }

        private static string ScanNcName(string expression, ref int index)
        {
            int start = index;
            while (index < expression.Length && IsNameChar(expression[index]))
            {
                index++;
            }

            return expression[start..index];
        }

        /// <summary>
        /// Reports a lexical failure, which is always <c>XPST0003</c>.
        /// </summary>
        /// <remarks>
        /// Every error the scanner can raise is the same kind: text that no expression could contain. The
        /// grammar is what refuses it, so the code is the one the specification gives a static error in the
        /// grammar, and there is no second kind of failure here to distinguish it from.
        /// </remarks>
        private static XsltException Error(string expression, int position, string message)
        {
            return XsltErrors.Error(
                XsltErrorCode.XPST0003, $"{message} (at offset {position} in \"{expression}\")");
        }

        private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\r' or '\n';

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        /// <summary>
        /// Reads <c>Q{uri}local</c>, the index on the <c>Q</c>.
        /// </summary>
        /// <remarks>
        /// The URI is whitespace-collapsed, being an <c>xs:anyURI</c>: the ends are trimmed and each run
        /// inside becomes one space, so <c>Q{ urn:foo bar }a</c> and <c>Q{urn:foo   bar}a</c> are the same
        /// name. Nothing else about it is interpreted — <c>Q{foo%20bar}</c> and <c>Q{foo bar}</c> are two
        /// namespaces, the per-cent escape being a character the URI happens to contain.
        /// </remarks>
        private static XPathToken ScanBracedName(string expression, ref int index)
        {
            int start = index;
            index += 2;
            int close = expression.IndexOf('}', index);

            if (close < 0)
            {
                throw Error(expression, start, "A 'Q{' names a namespace and is closed by '}'.");
            }

            // A brace inside the braces closes nothing and is not part of a URI either, so a second one is
            // a mistake rather than a character.
            if (expression.AsSpan(index, close - index).Contains('{'))
            {
                throw Error(expression, start, "A namespace written as 'Q{…}' holds no brace of its own.");
            }

            string uri = Collapse(expression[index..close]);
            index = close + 1;

            // Q{uri}* is the wildcard over one namespace, the braced spelling of prefix:*.
            if (index < expression.Length && expression[index] == '*')
            {
                index++;
                return new XPathToken(XPathTokenKind.Name, start, uri, "*") { NamespaceUri = uri };
            }

            if (index >= expression.Length || !IsNameStart(expression[index]))
            {
                throw Error(expression, start, "A namespace written as 'Q{…}' is followed by a local name.");
            }

            int nameStart = index;
            while (index < expression.Length && IsNameChar(expression[index]))
            {
                index++;
            }

            // The prefix carries the URI too, so that every test asking whether the name has a namespace
            // still answers yes without knowing about this form.
            return new XPathToken(XPathTokenKind.Name, start, uri, expression[nameStart..index])
            {
                NamespaceUri = uri,
            };
        }

        /// <summary>Trims a string and reduces each run of whitespace inside it to one space.</summary>
        private static string Collapse(string text)
        {
            System.Text.StringBuilder collapsed = new System.Text.StringBuilder(text.Length);
            bool spaced = false;

            foreach (char character in text)
            {
                if (character is ' ' or '\t' or '\r' or '\n')
                {
                    spaced = collapsed.Length > 0;
                    continue;
                }

                if (spaced)
                {
                    collapsed.Append(' ');
                    spaced = false;
                }

                collapsed.Append(character);
            }

            return collapsed.ToString();
        }

        // XML's name characters, not .NET's letters: the micro sign is a letter to char.IsLetter and not a
        // name character to XML, and a QName is an XML name (XPST0003 otherwise).
        private static bool IsNameStart(char c) => System.Xml.XmlConvert.IsStartNCNameChar(c);

        private static bool IsNameChar(char c) => System.Xml.XmlConvert.IsNCNameChar(c);
    }
}
