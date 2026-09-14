using System.Text;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// An attribute value template — literal text with embedded expressions in braces, as in
    /// <c>href="{@url}/index.html"</c>.
    /// </summary>
    /// <remarks>
    /// A doubled brace is an escaped literal brace. A template with no expressions is recognised as constant
    /// so that the common case costs nothing at run time.
    /// </remarks>
    internal sealed class AttributeValueTemplate
    {
        private readonly object[] m_parts;

        /// <summary>
        /// Whether an embedded expression contributes only its first item.
        /// </summary>
        /// <remarks>
        /// XSLT 2.0 joins the items of a sequence with spaces; 1.0 had no sequences and took the
        /// string-value of the first node of the node-set. Settled where the template is parsed, the
        /// version being a property of where it was written.
        /// </remarks>
        private readonly bool m_firstItemOnly;

        private AttributeValueTemplate(object[] parts, string? constantValue, bool firstItemOnly = false)
        {
            m_parts = parts;
            ConstantValue = constantValue;
            m_firstItemOnly = firstItemOnly;
        }

        /// <summary>
        /// Gets the fixed value when the template contains no expressions, otherwise <see langword="null"/>.
        /// </summary>
        public string? ConstantValue { get; }

        /// <summary>
        /// Parses an attribute value template.
        /// </summary>
        /// <param name="text">The attribute's literal text.</param>
        /// <param name="context">The static context used to compile embedded expressions.</param>
        /// <returns>The compiled template.</returns>
        /// <exception cref="XsltException">A brace is unbalanced, or an embedded expression is invalid.</exception>
        public static AttributeValueTemplate Parse(
            string text,
            IXPathStaticContext context,
            XsltBackend backend = XsltBackend.Interpreted)
        {
            if (text.IndexOf('{') < 0 && text.IndexOf('}') < 0)
            {
                return new AttributeValueTemplate(Array.Empty<object>(), text);
            }

            List<object> parts = new List<object>();
            StringBuilder literal = new StringBuilder();
            int index = 0;

            while (index < text.Length)
            {
                char c = text[index];

                if (c == '{')
                {
                    if (index + 1 < text.Length && text[index + 1] == '{')
                    {
                        literal.Append('{');
                        index += 2;
                        continue;
                    }

                    int close = FindClosingBrace(text, index + 1);
                    if (close < 0)
                    {
                        throw XsltErrors.Error(XsltErrorCode.XTSE0350, $"Unclosed '{{' in attribute value template \"{text}\".");
                    }

                    string embedded = text[(index + 1)..close];

                    // XSLT 3.0 lets the expression be absent, and gives it the empty sequence: x{}y is xy,
                    // and so is x{ (:nothing:) }y. There is nothing to parse and nothing to evaluate, so
                    // the braces contribute no part at all and the literal text runs on through them.
                    if (IsAbsent(embedded) && context.SyntaxVersion.CompareTo(XsltVersion.V30) >= 0)
                    {
                        index = close + 1;
                        continue;
                    }

                    if (literal.Length != 0)
                    {
                        parts.Add(literal.ToString());
                        literal.Clear();
                    }

                    Expr parsed = XPathParser.Parse(embedded, context);
                    parts.Add(backend == XsltBackend.Compiled
                        ? Emit.ExpressionCompiler.Compile(parsed, embedded)
                        : parsed);

                    index = close + 1;
                    continue;
                }

                if (c == '}')
                {
                    if (index + 1 < text.Length && text[index + 1] == '}')
                    {
                        literal.Append('}');
                        index += 2;
                        continue;
                    }

                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0370,
                        $"A '}}' in an attribute value template must be doubled: \"{text}\".");
                }

                literal.Append(c);
                index++;
            }

            // Nothing but literal text is a constant however it was written — which a template whose only
            // braces held absent expressions now is, and which is what keeps x{}y as cheap as xy.
            if (parts.Count == 0)
            {
                return new AttributeValueTemplate(Array.Empty<object>(), literal.ToString());
            }

            if (literal.Length != 0)
            {
                parts.Add(literal.ToString());
            }

            return new AttributeValueTemplate(parts.ToArray(), null, context.Version.IsBackwardsCompatible);
        }

        /// <summary>Evaluates the template against a context.</summary>
        /// <param name="context">The context embedded expressions are evaluated in.</param>
        /// <returns>The resulting attribute value.</returns>
        public string Evaluate(ref DynamicContext context)
        {
            if (ConstantValue is not null)
            {
                return ConstantValue;
            }

            // A template alternates literal text with expressions, so it has few parts — usually two, and
            // rarely more than four. string.Concat has an overload for each of those counts that measures
            // the total, allocates once and copies in, which is all a builder was doing here. Arguments are
            // evaluated left to right, so the parts still reach it in the order they were written.
            switch (m_parts.Length)
            {
                case 1:
                    return TextOf(m_parts[0], ref context);

                case 2:
                    return string.Concat(
                        TextOf(m_parts[0], ref context),
                        TextOf(m_parts[1], ref context));

                case 3:
                    return string.Concat(
                        TextOf(m_parts[0], ref context),
                        TextOf(m_parts[1], ref context),
                        TextOf(m_parts[2], ref context));

                case 4:
                    return string.Concat(
                        TextOf(m_parts[0], ref context),
                        TextOf(m_parts[1], ref context),
                        TextOf(m_parts[2], ref context),
                        TextOf(m_parts[3], ref context));

                default:
                    return EvaluateMany(ref context);
            }
        }

        /// <summary>Evaluates a template with more parts than <see cref="string.Concat(string?[])"/> names.</summary>
        /// <param name="context">The context embedded expressions are evaluated in.</param>
        private string EvaluateMany(ref DynamicContext context)
        {
            string[] parts = new string[m_parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = TextOf(m_parts[i], ref context);
            }

            return string.Concat(parts);
        }

        /// <summary>Returns what a part contributes: its literal text, or what its expression evaluates to.</summary>
        /// <param name="part">The part, which is either a <see cref="string"/> or an <see cref="Expr"/>.</param>
        /// <param name="context">The context an expression is evaluated in.</param>
        private string TextOf(object part, ref DynamicContext context)
        {
            if (part is not Expr expression)
            {
                return (string)part;
            }

            XPathValue value = expression.Evaluate(ref context);

            // Simple content with a single space between items — so a function returning adjacent text
            // nodes reads as their text, merged, rather than as items spaced apart.
            return m_firstItemOnly
                ? XdmSequence.FirstItem(value).ToCanonicalString()
                : XdmSequence.SimpleContent(value, " ");
        }

        /// <summary>
        /// Finds the brace closing an embedded expression, skipping over any braces inside string literals so
        /// that <c>{@x = '}'}</c> is read correctly.
        /// </summary>
        /// <remarks>
        /// Braces inside the expression are counted rather than ended on, because XPath 3.1 writes a map with
        /// them: the closing brace of <c>{map { 'a': 1 }?a}</c> is the second one, not the first.
        /// <para>
        /// A comment is skipped whole, and comments nest, so a brace or a quotation mark inside one says
        /// nothing about where the expression ends. The suite writes
        /// <c>{('exp', (: comments isn't }fun{ :) 23)}</c>, where every one of those would otherwise be
        /// read as itself.
        /// </para>
        /// </remarks>
        private static int FindClosingBrace(string text, int start)
        {
            char quote = '\0';
            int depth = 0;
            int comment = 0;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];

                if (comment > 0)
                {
                    if (Opens(text, i))
                    {
                        comment++;
                        i++;
                    }
                    else if (c == ':' && i + 1 < text.Length && text[i + 1] == ')')
                    {
                        comment--;
                        i++;
                    }

                    continue;
                }

                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (Opens(text, i))
                {
                    comment++;
                    i++;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    continue;
                }

                if (c == '}' && depth > 0)
                {
                    depth--;
                    continue;
                }

                if (c is '\'' or '"')
                {
                    quote = c;
                    continue;
                }

                if (c == '}')
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Whether a comment begins at a position in the text.</summary>
        /// <param name="text">The text being scanned.</param>
        /// <param name="index">The position to look at.</param>
        private static bool Opens(string text, int index)
        {
            return text[index] == '(' && index + 1 < text.Length && text[index + 1] == ':';
        }

        /// <summary>
        /// Whether an embedded expression is absent — whitespace and comments, and nothing else.
        /// </summary>
        /// <remarks>
        /// XPath has no empty expression, so this is asked before the parser is: XSLT 3.0 allows the braces
        /// to hold nothing and gives that the empty sequence, where the parser can only report a syntax
        /// error for it. An unterminated comment is not absent, and is left for the parser to complain
        /// about in its own words.
        /// </remarks>
        /// <param name="expression">The text between the braces.</param>
        private static bool IsAbsent(string expression)
        {
            int comment = 0;

            for (int i = 0; i < expression.Length; i++)
            {
                if (Opens(expression, i))
                {
                    comment++;
                    i++;
                    continue;
                }

                if (comment > 0)
                {
                    if (expression[i] == ':' && i + 1 < expression.Length && expression[i + 1] == ')')
                    {
                        comment--;
                        i++;
                    }

                    continue;
                }

                if (!char.IsWhiteSpace(expression[i]))
                {
                    return false;
                }
            }

            return comment == 0;
        }
    }
}
