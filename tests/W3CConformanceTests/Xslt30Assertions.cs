using System.Text;
using System.Xml;
using System.Xml.Linq;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Conformance
{
    /// <summary>
    /// Checks an XSLT 3.0 suite result assertion against what this engine produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The suite asks most of its questions in XPath: <c>assert</c> holds an expression over the result
    /// document, and it is two-thirds of every assertion in the suite. Answering those means using this
    /// engine's own XPath to judge its own XSLT, which is sound here for a reason worth stating — the XPath
    /// half is separately measured against QT3, so it is a checked tool rather than an unchecked assumption.
    /// The XSLT half has had no such measure until now, which is the point of this driver.
    /// </para>
    /// <para>
    /// What the driver cannot present fairly it skips. That includes every assertion about a result as a
    /// typed sequence — <c>assert-type</c>, <c>assert-eq</c>, <c>assert-count</c> — because a transformation
    /// here writes a document rather than handing back a value, so the sequence those ask about is gone by
    /// the time the driver can see anything.
    /// </para>
    /// </remarks>
    internal static class Xslt30Assertions
    {
        public static TestResult Check(XElement assertion, Transformation outcome)
        {
            switch (assertion.Name.LocalName)
            {
                case "all-of":
                {
                    TestResult? skipped = null;

                    foreach (XElement child in assertion.Elements())
                    {
                        TestResult one = Check(child, outcome);
                        if (one.Outcome == Outcome.Failed)
                        {
                            return one;
                        }

                        skipped ??= one.Outcome == Outcome.Skipped ? one : null;
                    }

                    return skipped ?? Pass();
                }

                case "any-of":
                {
                    TestResult? failure = null;
                    TestResult? unpresentable = null;

                    foreach (XElement child in assertion.Elements())
                    {
                        TestResult one = Check(child, outcome);
                        if (one.Outcome == Outcome.Passed)
                        {
                            return one;
                        }

                        if (one.Outcome == Outcome.Skipped)
                        {
                            unpresentable ??= one;
                        }
                        else
                        {
                            failure ??= one;
                        }
                    }

                    // A branch the driver could not present might have been the one that held, so the test
                    // is skipped rather than failed: any-of asks whether *some* branch holds, and one of
                    // them was never asked. Calling it a failure would assert what the driver did not check.
                    return unpresentable ?? failure ?? Skip("any-of had no branches");
                }

                case "not":
                {
                    TestResult inner = Check(assertion.Elements().First(), outcome);
                    return inner.Outcome switch
                    {
                        Outcome.Passed => Fail("the assertion held where it was asserted not to"),
                        Outcome.Failed => Pass(),
                        _ => inner,
                    };
                }

                case "error":
                    return CheckError(assertion, outcome);

                case "assert-result-document":
                    return CheckResultDocument(assertion, outcome);

                case "assert":
                    return CheckExpression(assertion, outcome);

                case "assert-xml":
                    return CheckXml(assertion, outcome);

                case "assert-string-value":
                    return CheckStringValue(assertion, outcome);

                case "serialization-matches":
                    return CheckSerializationMatches(assertion, outcome);

                case "assert-serialization":
                    return CheckSerialization(assertion, outcome);

                case "assert-message":
                    return CheckMessage(assertion, outcome);

                default:
                    return Skip($"assertion '{assertion.Name.LocalName}' is not one this driver can present");
            }
        }

        // ---- The assertions ------------------------------------------------------------------------------


        /// <summary>
        /// Puts the assertion inside an <c>assert-message</c> to what <c>xsl:message</c> wrote rather
        /// than to the result.
        /// </summary>
        /// <remarks>
        /// A message is a result of its own, and the catalog asks about it with the same assertions it
        /// asks about the principal one, so the inner assertion is answered by standing the messages in
        /// the result's place. Everything the run wrote is one string here: the tests asking this write
        /// one message, and a driver splitting them would have to invent where one ends.
        /// </remarks>
        private static TestResult CheckMessage(XElement assertion, Transformation outcome)
        {
            if (outcome.Messages.Count == 0)
            {
                return outcome.Error is not null
                    ? Fail($"error raised: {outcome.Error}")
                    : Fail("the transformation wrote no message");
            }

            if (assertion.Elements().FirstOrDefault() is not XElement inner)
            {
                return Pass();
            }

            // The catalog names one message and a run may write several, so it holds of the run if it
            // holds of any of them. Which one it is about the catalog does not say.
            TestResult last = Fail("no message answered the assertion");

            foreach (string message in outcome.Messages)
            {
                last = Check(
                    inner,
                    new Transformation
                    {
                        Result = message,
                        Directory = outcome.Directory,
                        ResultUri = outcome.ResultUri,
                        Schemas = outcome.Schemas,
                    });

                if (last.Outcome == Outcome.Passed)
                {
                    return last;
                }
            }

            return last;
        }

        private static TestResult CheckError(XElement assertion, Transformation outcome)
        {
            string expected = (string?)assertion.Attribute("code") ?? "*";

            // A code the catalog writes in the Q{uri}local form, which xsl:message error-code allows a
            // stylesheet to invent. In no namespace it is the local name, which is what the engine
            // reports; in one this driver cannot present it, the engine carrying codes as names alone.
            if (expected.StartsWith("Q{}", StringComparison.Ordinal))
            {
                expected = expected["Q{}".Length..];
            }

            if (outcome.Error is null)
            {
                return Fail($"expected error {expected}, but the transformation completed");
            }

            if (outcome.ErrorCode is null)
            {
                // The right thing happened and whether it happened for the right reason is unknown, so
                // counting it either way would be a guess. These skips are the work queue for the codes.
                return Skip(
                    $"raised an error with no code, where {expected} was expected: "
                    + Flat(outcome.Error ?? string.Empty));
            }

            // XXXX9999 is the catalog's way of saying an error is required and the specification names
            // none in particular, so any code answers it.
            return expected is "*" or "XXXX9999"
                || string.Equals(expected, outcome.ErrorCode, StringComparison.Ordinal)
                    ? Pass()
                    // With the message, because a code that is wrong is a code the engine chose for
                    // a reason, and the reason is what says which of the two is mistaken.
                    : Fail($"expected error {expected}, got {outcome.ErrorCode}: {Flat(outcome.Error ?? string.Empty)}");
        }

        private static TestResult CheckResultDocument(XElement assertion, Transformation outcome)
        {
            if (outcome.Error is not null)
            {
                return Fail($"error raised: {outcome.Error}");
            }

            string uri = (string?)assertion.Attribute("uri") ?? string.Empty;

            // The href the stylesheet wrote is what the collector keyed on, and the assertion names the same
            // document relative to a base output URI the driver never made absolute. Matching on the tail
            // covers the difference without letting two different documents pass for each other.
            KeyValuePair<string, string> found = outcome.ResultDocuments.FirstOrDefault(
                entry => entry.Key == uri
                    || entry.Key.EndsWith("/" + uri, StringComparison.Ordinal)
                    || uri.EndsWith("/" + entry.Key, StringComparison.Ordinal));

            if (found.Key is null)
            {
                return Fail(
                    $"no result document '{uri}' was written"
                    + (outcome.ResultDocuments.Count == 0
                        ? string.Empty
                        : $"; wrote {string.Join(", ", outcome.ResultDocuments.Keys)}"));
            }

            return Check(
                assertion.Elements().First(),
                new Transformation { Result = found.Value, Directory = outcome.Directory, ResultUri = found.Key });
        }

        private static TestResult CheckExpression(XElement assertion, Transformation outcome)
        {
            if (outcome.Error is not null)
            {
                return Fail($"error raised: {outcome.Error}");
            }

            XdmTree? tree = Parse(outcome.Result);

            if (tree is null)
            {
                return Skip("the result is not well-formed XML, so an XPath assertion cannot be put to it");
            }

            // A result document knows where it was written, which is what base-uri() of its nodes is.
            tree.DocumentUri = outcome.ResultUri;

            // The catalog states 3.0 whichever subset is being run: the assertion is the suite's language,
            // not the stylesheet's, and reading it should not depend on which tests were selected.
            // The environment's schemas, where the run has any: an assertion may name a declaration out
            // of them, as validation-1601 asks whether the result is a document-node(schema-element(doc)),
            // and without them the question is unreadable rather than false.
            XPathStaticContext staticContext = new XPathStaticContext
            {
                Version = XsltVersion.V30,
                Schemas = outcome.Schemas,
            };

            foreach (XAttribute attribute in InScopeNamespaces(assertion))
            {
                // The default namespace is always no-namespace for these expressions, as the catalog schema
                // states, so only prefixed declarations are carried over.
                if (attribute.Name.Namespace == XNamespace.Xmlns)
                {
                    staticContext.DeclarePrefix(attribute.Name.LocalName, attribute.Value);
                }
            }

            // The catalog lets an assertion name the result as $result as well as stand on it.
            staticContext.DeclareGlobalVariable("result", 0);

            try
            {
                Expr compiled = XPathParser.Parse(assertion.Value, staticContext);
                DynamicContext context = new DynamicContext(
                    tree, XdmTree.RootNode, staticContext.Names.BuildFingerprintMap(tree), staticContext.Names)
                {
                    Globals = new[] { XPathValue.FromNode(tree, XdmTree.RootNode) },
                };

                if (compiled.Evaluate(ref context).ToBoolean())
                {
                    return Pass();
                }

                // Serialized output carries no type annotations, so an assertion about them is answered
                // no by any reparse however the transformation went. Where the run can offer the result
                // as the transformation left it, the question is put again to that. The two are the same
                // result in two renderings, and one the engine satisfies in either it satisfies.
                return AskTheResultTree(compiled, staticContext, outcome)
                    ? Pass()
                    : Fail($"assertion is false: {Flat(assertion.Value)}");
            }
            catch (XsltException exception)
            {
                // The assertion is the driver's instrument. One this engine cannot read measures the
                // instrument rather than the test, so it is skipped and named.
                return Skip($"the assertion expression could not be evaluated: {Flat(exception.Message)}");
            }
        }


        /// <summary>
        /// Puts an assertion that the serialized result answered no to the result as a tree, which is
        /// what carries the type annotations a schema-aware transformation settled.
        /// </summary>
        private static bool AskTheResultTree(Expr compiled, XPathStaticContext staticContext, Transformation outcome)
        {
            if (outcome.Tree?.Invoke() is not XdmTree tree)
            {
                return false;
            }

            tree.DocumentUri = outcome.ResultUri;

            try
            {
                DynamicContext context = new DynamicContext(
                    tree, XdmTree.RootNode, staticContext.Names.BuildFingerprintMap(tree), staticContext.Names)
                {
                    Globals = new[] { XPathValue.FromNode(tree, XdmTree.RootNode) },
                };

                return compiled.Evaluate(ref context).ToBoolean();
            }
            catch (XsltException)
            {
                return false;
            }
        }

        /// <summary>Compares the result with the XML the test says it should be.</summary>
        private static TestResult CheckXml(XElement assertion, Transformation outcome)
        {
            if (outcome.Error is not null)
            {
                return Fail($"error raised: {outcome.Error}");
            }

            string? expected = Expected(assertion, outcome);
            if (expected is null)
            {
                return Skip("the expected result file is not in the suite");
            }

            bool ignorePrefixes = (string?)assertion.Attribute("ignore-prefixes") == "true";

            string? wanted = Canonical(expected, ignorePrefixes);
            string? got = Canonical(outcome.Result, ignorePrefixes);

            if (wanted is null)
            {
                return Skip("the expected result is not well-formed XML");
            }

            if (got is null)
            {
                return Fail($"the result is not well-formed XML: {Flat(outcome.Result)}");
            }

            return string.Equals(wanted, got, StringComparison.Ordinal)
                ? Pass()
                : Fail(Difference(wanted, got));
        }

        private static TestResult CheckStringValue(XElement assertion, Transformation outcome)
        {
            if (outcome.Error is not null)
            {
                return Fail($"error raised: {outcome.Error}");
            }

            // A result that is not XML — the text output method's, say — is its own string value.
            XdmTree? tree = Parse(outcome.Result);
            string got;

            if (tree is null)
            {
                if (outcome.Result is null)
                {
                    return Skip("the result is not well-formed XML, so it has no string value to read");
                }

                got = outcome.Result;
            }
            else
            {
                XPathStaticContext staticContext = new XPathStaticContext { Version = XsltVersion.V30 };
                Expr compiled = XPathParser.Parse("string(.)", staticContext);
                DynamicContext context = new DynamicContext(
                    tree, XdmTree.RootNode, staticContext.Names.BuildFingerprintMap(tree), staticContext.Names);

                got = compiled.Evaluate(ref context).ToStringValue();
            }
            string expected = assertion.Value;

            // Normalized unless the assertion says otherwise, which is the suite's own default and not a
            // convenience: its catalog schema declares normalize-space with default="true". Reading it the
            // other way made every test whose result carries the source document's indentation fail on
            // whitespace, and report two strings that look identical.
            if ((string?)assertion.Attribute("normalize-space") is not "false")
            {
                got = NormalizeSpace(got);
                expected = NormalizeSpace(expected);
            }

            return string.Equals(expected, got, StringComparison.Ordinal)
                ? Pass()
                : Fail($"expected string value '{Flat(expected)}', got '{Flat(got)}'");
        }

        private static TestResult CheckSerializationMatches(XElement assertion, Transformation outcome)
        {
            if (outcome.Error is not null)
            {
                return Fail($"error raised: {outcome.Error}");
            }

            string flags = (string?)assertion.Attribute("flags") ?? string.Empty;

            // Matched by this engine's own matches(), rather than by .NET's regular expressions directly:
            // the two languages disagree on enough — a character is a code point in one and a UTF-16 unit in
            // the other, \w and \s name different sets — that reading the suite's pattern with the wrong one
            // would decide some of these tests by the driver's mistake.
            string expression =
                $"matches({Literal(outcome.Result)}, {Literal(assertion.Value)}, {Literal(flags)})";

            try
            {
                XPathStaticContext staticContext = new XPathStaticContext { Version = XsltVersion.V30 };
                Expr compiled = XPathParser.Parse(expression, staticContext);
                XdmTree tree = XdmTreeBuilder.FromXml("<empty/>", false);

                DynamicContext context = new DynamicContext(
                    tree,
                    DynamicContext.NotANode,
                    staticContext.Names.BuildFingerprintMap(tree),
                    staticContext.Names);

                return compiled.Evaluate(ref context).ToBoolean()
                    ? Pass()
                    : Fail($"the result does not match /{Flat(assertion.Value)}/: {Flat(outcome.Result)}");
            }
            catch (XsltException exception)
            {
                return Skip($"the expected pattern could not be read: {Flat(exception.Message)}");
            }
        }

        private static TestResult CheckSerialization(XElement assertion, Transformation outcome)
        {
            if (outcome.Error is not null)
            {
                return Fail($"error raised: {outcome.Error}");
            }

            string? expected = Expected(assertion, outcome);
            if (expected is null)
            {
                return Skip("the expected result file is not in the suite");
            }

            // Line endings and a trailing newline are what the assertion's own documentation calls
            // differences a conformant implementation may produce, so they are absorbed and nothing else is.
            return string.Equals(Lines(expected), Lines(outcome.Result), StringComparison.Ordinal)
                ? Pass()
                : Fail(Difference(Lines(expected), Lines(outcome.Result)));
        }

        /// <summary>
        /// Says where two results first differ, rather than showing the front of both.
        /// </summary>
        /// <remarks>
        /// Two documents that differ in one space read as identical when both are printed and the difference
        /// is past the width of the line — or is the whitespace itself. Naming the position and making the
        /// whitespace visible is what turns such a failure into something that can be looked into.
        /// </remarks>
        private static string Difference(string wanted, string got)
        {
            int at = 0;
            while (at < wanted.Length && at < got.Length && wanted[at] == got[at])
            {
                at++;
            }

            return $"differ at character {at}: expected {Window(wanted, at)}, got {Window(got, at)}";
        }

        private static string Window(string text, int at)
        {
            int from = Math.Max(0, at - 20);
            int to = Math.Min(text.Length, at + 60);

            return (from > 0 ? "…" : string.Empty)
                + Visible(text[from..to])
                + (to < text.Length ? "…" : string.Empty);
        }

        private static string Visible(string text) => text
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

        // ---- Reading the result --------------------------------------------------------------------------

        /// <summary>Parses a serialized result, which may be a fragment rather than a whole document.</summary>
        private static XdmTree? Parse(string result)
        {
            try
            {
                return XdmTreeBuilder.FromXml(StripDeclaration(result), fragment: true);
            }
            catch (XmlException)
            {
                return null;
            }
        }

        /// <summary>
        /// Rewrites a serialized document into a form two of them can be compared in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Attribute order, namespace declaration order, the empty-element form and the choice of which
        /// characters to escape are all free to a serializer, so a comparison that saw them would decide
        /// tests on nothing. Whitespace is not free and is kept exactly: a test whose stylesheet asks for
        /// indentation has an expected result with the indentation in it.
        /// </para>
        /// <para>
        /// Outside the document element it is not content at all. XML allows whitespace on either side of
        /// the outermost element, and nothing in a result tree corresponds to it — not the newline a text
        /// file ends with, nor the one a catalog's CDATA section picks up from being typed on its own line.
        /// Comparing that would decide tests on how the expectation was written down. It is dropped only
        /// where there is an outermost element to be outside of: a result that is a bare sequence of text
        /// keeps every character of it.
        /// </para>
        /// </remarks>
        private static string? Canonical(string xml, bool ignorePrefixes)
        {
            try
            {
                XElement wrapper = XElement.Parse(
                    "<canonical-wrapper>" + StripDeclaration(xml) + "</canonical-wrapper>",
                    LoadOptions.PreserveWhitespace);

                int elements = 0;
                foreach (XNode node in wrapper.Nodes())
                {
                    if (node is XElement)
                    {
                        elements++;
                    }
                }

                StringBuilder builder = new StringBuilder();
                foreach (XNode node in wrapper.Nodes())
                {
                    if (elements == 1 && node is XText outside && outside.Value.Trim().Length == 0)
                    {
                        continue;
                    }

                    Write(node, builder, ignorePrefixes);
                }

                return builder.ToString();
            }
            catch (XmlException)
            {
                return null;
            }
        }

        private static void Write(XNode node, StringBuilder builder, bool ignorePrefixes)
        {
            switch (node)
            {
                case XElement element:
                {
                    string name = Name(element.Name, element.GetPrefixOfNamespace(element.Name.Namespace),
                        ignorePrefixes);

                    builder.Append('<').Append(name);

                    List<XAttribute> declarations = new();
                    List<XAttribute> attributes = new();

                    foreach (XAttribute attribute in element.Attributes())
                    {
                        (attribute.IsNamespaceDeclaration ? declarations : attributes).Add(attribute);
                    }

                    if (!ignorePrefixes)
                    {
                        foreach (XAttribute declaration in declarations.OrderBy(a => a.Name.LocalName, StringComparer.Ordinal))
                        {
                            builder.Append(' ').Append(declaration.Name.LocalName == "xmlns"
                                ? "xmlns"
                                : "xmlns:" + declaration.Name.LocalName);
                            builder.Append("=\"").Append(Escape(declaration.Value, true)).Append('"');
                        }
                    }

                    foreach (XAttribute attribute in attributes
                        .OrderBy(a => a.Name.NamespaceName, StringComparer.Ordinal)
                        .ThenBy(a => a.Name.LocalName, StringComparer.Ordinal))
                    {
                        builder.Append(' ')
                            .Append(Name(attribute.Name, element.GetPrefixOfNamespace(attribute.Name.Namespace),
                                ignorePrefixes))
                            .Append("=\"")
                            .Append(Escape(attribute.Value, true))
                            .Append('"');
                    }

                    builder.Append('>');

                    // Whitespace between element children is layout — indentation the serializer added,
                    // or whitespace the source carried between elements — and the expected results are
                    // written without it. Whitespace that is an element's whole content stays significant.
                    bool hasElementChildren = element.Elements().Any();

                    foreach (XNode child in element.Nodes())
                    {
                        if (hasElementChildren && child is XText layout && layout.Value.Trim().Length == 0)
                        {
                            continue;
                        }

                        Write(child, builder, ignorePrefixes);
                    }

                    builder.Append("</").Append(name).Append('>');
                    break;
                }

                case XText text:
                    builder.Append(Escape(text.Value, false));
                    break;

                case XComment comment:
                    builder.Append("<!--").Append(comment.Value).Append("-->");
                    break;

                case XProcessingInstruction instruction:
                    builder.Append("<?").Append(instruction.Target).Append(' ').Append(instruction.Data).Append("?>");
                    break;
            }
        }

        private static string Name(XName name, string? prefix, bool ignorePrefixes)
        {
            if (ignorePrefixes)
            {
                return name.NamespaceName.Length == 0 ? name.LocalName : "{" + name.NamespaceName + "}" + name.LocalName;
            }

            return string.IsNullOrEmpty(prefix) ? name.LocalName : prefix + ":" + name.LocalName;
        }

        private static string Escape(string text, bool attribute)
        {
            StringBuilder builder = new StringBuilder(text.Length);

            foreach (char character in text)
            {
                switch (character)
                {
                    case '&': builder.Append("&amp;"); break;
                    case '<': builder.Append("&lt;"); break;
                    case '>': builder.Append("&gt;"); break;
                    case '"' when attribute: builder.Append("&quot;"); break;
                    case '\r': builder.Append("&#xD;"); break;
                    case '\n' when attribute: builder.Append("&#xA;"); break;
                    case '\t' when attribute: builder.Append("&#x9;"); break;
                    default: builder.Append(character); break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Removes an XML declaration from the front of a serialized result.
        /// </summary>
        /// <remarks>
        /// <c>assert-xml</c> is written against a result serialized with <c>omit-xml-declaration="yes"</c>,
        /// and this engine writes one unless the stylesheet said not to. Dropping it here compares the two
        /// documents rather than the two prologues.
        /// </remarks>
        private static string StripDeclaration(string xml)
        {
            string trimmed = xml.TrimStart('﻿', ' ', '\t', '\r', '\n');

            if (!trimmed.StartsWith("<?xml ", StringComparison.Ordinal)
                && !trimmed.StartsWith("<?xml?", StringComparison.Ordinal))
            {
                return xml;
            }

            int close = trimmed.IndexOf("?>", StringComparison.Ordinal);
            return close < 0 ? xml : trimmed[(close + 2)..].TrimStart('\r', '\n');
        }

        /// <summary>Reads the expected result, which the assertion holds inline or names a file for.</summary>
        private static string? Expected(XElement assertion, Transformation outcome)
        {
            if ((string?)assertion.Attribute("file") is not string file)
            {
                return assertion.Value;
            }

            string path = Path.Combine(outcome.Directory, file.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                return null;
            }

            // A serialization assertion says what encoding its file is in; read as UTF-8, a Latin-1 byte
            // above 0x7F is a replacement character and the comparison is decided by the driver's mistake.
            try
            {
                return (string?)assertion.Attribute("encoding") is string encoding
                    ? File.ReadAllText(path, System.Text.Encoding.GetEncoding(encoding))
                    : File.ReadAllText(path);
            }
            catch (ArgumentException)
            {
                return File.ReadAllText(path);
            }
        }

        /// <summary>Writes a string as an XPath literal, so a pattern can be put to the engine's matches().</summary>
        private static string Literal(string text) =>
            "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

        private static IEnumerable<XAttribute> InScopeNamespaces(XElement element)
        {
            for (XElement? current = element; current is not null; current = current.Parent)
            {
                foreach (XAttribute attribute in current.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        yield return attribute;
                    }
                }
            }
        }

        private static string Lines(string text) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

        private static string NormalizeSpace(string text) =>
            string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private static string Flat(string text)
        {
            string flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return flat.Length > 120 ? flat[..120] + "…" : flat;
        }

        private static TestResult Pass() => new TestResult(Outcome.Passed, string.Empty);

        private static TestResult Fail(string detail) => new TestResult(Outcome.Failed, detail);

        private static TestResult Skip(string detail) => new TestResult(Outcome.Skipped, detail);
    }
}
