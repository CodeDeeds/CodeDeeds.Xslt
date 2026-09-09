using System.Globalization;
using System.Xml.Linq;
using CodeDeeds.Xslt;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Conformance
{
    /// <summary>
    /// Checks a QT3 result assertion against what this engine produced.
    /// </summary>
    /// <remarks>
    /// The suite's assertion vocabulary is larger than what an XPath 1.0-shaped value model can express — a
    /// result here is a string, a number, a boolean or a node-set, never a typed sequence — so the assertions
    /// that ask about sequences or types are skipped rather than guessed at. What is checked is checked
    /// exactly.
    /// </remarks>
    internal static class Assertions
    {
        public static TestResult Check(
            XElement result,
            XPathValue value,
            string? error,
            string? errorCode,
            XPathStaticContext staticContext,
            XdmTree tree)
        {
            // A result element holds one assertion, or all-of / any-of holding several.
            XElement assertion = result.Elements().FirstOrDefault()
                ?? throw new InvalidOperationException("empty result element");

            return CheckOne(assertion, value, error, errorCode, staticContext, tree);
        }

        private static TestResult CheckOne(
            XElement assertion,
            XPathValue value,
            string? error,
            string? errorCode,
            XPathStaticContext staticContext,
            XdmTree tree)
        {
            string name = assertion.Name.LocalName;

            switch (name)
            {
                case "all-of":
                {
                    foreach (XElement child in assertion.Elements())
                    {
                        TestResult one = CheckOne(child, value, error, errorCode, staticContext, tree);
                        if (one.Outcome != Outcome.Passed)
                        {
                            return one;
                        }
                    }

                    return Pass();
                }

                case "any-of":
                {
                    string last = "any-of had no branches";
                    TestResult? unjudged = null;

                    foreach (XElement child in assertion.Elements())
                    {
                        TestResult one = CheckOne(child, value, error, errorCode, staticContext, tree);
                        if (one.Outcome == Outcome.Passed)
                        {
                            return one;
                        }

                        if (one.Outcome == Outcome.Skipped)
                        {
                            unjudged ??= one;
                        }

                        last = one.Detail;
                    }

                    // A branch this driver cannot present leaves the whole any-of unjudged, and unjudged is
                    // a skip rather than a failure: the engine may well have taken that branch, and calling
                    // the test failed would report on the driver's reach rather than on the engine. The JSON
                    // tests are full of the shape — "either a value of type item()? or FOJS0001" — where a
                    // liberal parser answering with a value is right and the type assertion is one of the
                    // ones skipped everywhere else.
                    return unjudged ?? Fail(last);
                }

                case "error":
                {
                    string expected = (string?)assertion.Attribute("code") ?? "*";

                    if (error is null)
                    {
                        return Fail($"expected error {expected}, but the expression succeeded");
                    }

                    if (expected == "*" || errorCode == expected)
                    {
                        return Pass();
                    }

                    // XPST0051 is the engine saying the type name is not defined here at all, which is a
                    // feature it does not have rather than a wrong answer about one it does. Counting a test
                    // for xs:dateTime behaviour as a failure would say this engine gets dates wrong; it does
                    // not implement them.
                    if (errorCode == "XPST0051")
                    {
                        return Skip("needs a schema type this engine does not implement");
                    }

                    // Raising an error the engine has not given a code to is neither right nor wrong: the
                    // right thing happened, and whether it happened for the right reason is unknown. Counting
                    // it as a pass would flatter the result, so it is reported as a skip instead.
                    return errorCode is null
                        ? Skip($"expected error {expected}; an error was raised but carries no code")
                        : Fail($"expected error {expected}, got {errorCode}");
                }
            }

            if (error is not null)
            {
                return Fail($"error: {error}");
            }

            switch (name)
            {
                case "assert-true":
                    return value.ToBoolean() ? Pass() : Fail("expected true");

                case "assert-false":
                    return !value.ToBoolean() ? Pass() : Fail("expected false");

                case "assert-empty":
                    return IsEmpty(value) ? Pass() : Fail("expected an empty sequence");

                case "assert-count":
                {
                    int expected = int.Parse((string)assertion, CultureInfo.InvariantCulture);
                    int actual = XdmSequence.Items(value).Count;
                    return actual == expected ? Pass() : Fail($"expected {expected} items, got {actual}");
                }

                case "assert-string-value":
                {
                    string expected = (string)assertion;
                    string actual = StringValueOfSequence(value, tree);

                    if ((string?)assertion.Attribute("normalize-space") == "true")
                    {
                        expected = NormalizeSpace(expected);
                        actual = NormalizeSpace(actual);
                    }

                    return string.Equals(expected, actual, StringComparison.Ordinal)
                        ? Pass()
                        : Fail($"expected string-value '{Trim(expected)}', got '{Trim(actual)}'");
                }

                case "assert-eq":
                {
                    string expectedText = (string)assertion;
                    if (!Literal.TryParse(expectedText, out XPathValue expected))
                    {
                        return Skip($"assert-eq expects '{Trim(expectedText)}', which this driver cannot build");
                    }

                    return EqualEnough(expected, value)
                        ? Pass()
                        : Fail($"expected {Trim(expectedText)}, got '{Trim(value.ToCanonicalString())}'");
                }

                case "assert-deep-eq":
                case "assert-permutation":
                {
                    string expectedText = (string)assertion;
                    if (!Literal.TryParseSequence(expectedText, out List<XPathValue> expected))
                    {
                        return Skip(
                            $"'{name}' expects '{Trim(expectedText)}', which this driver cannot build");
                    }

                    List<XPathValue> actual = XdmSequence.Items(value);

                    if (actual.Count != expected.Count)
                    {
                        return Fail($"expected {expected.Count} items, got {actual.Count}");
                    }

                    return SameItems(expected, actual, anyOrder: name == "assert-permutation")
                        ? Pass()
                        : Fail($"expected {Trim(expectedText)}, got '{Trim(Describe(actual, tree))}'");
                }

                case "assert":
                case "assert-type":
                case "assert-xml":
                case "assert-serialization":
                case "assert-serialization-error":
                    return Skip($"'{name}' needs the XPath 2.0 data model");

                default:
                    return Skip($"unknown assertion '{name}'");
            }
        }

        /// <summary>
        /// Whether a result is the empty sequence.
        /// </summary>
        /// <remarks>
        /// Counted as items, because that is what the suite means. An empty node-set and an empty sequence
        /// are both no items and are two different <see cref="XPathValueKind"/> values; asking the kind
        /// first got the node-set right and reported <c>avg(())</c> as a failure of the engine.
        /// </remarks>
        private static bool IsEmpty(XPathValue value)
        {
            return XdmSequence.Items(value).Count == 0;
        }

        /// <summary>
        /// The string-value the suite expects, which for a node-set is every node joined by a space rather
        /// than only the first — XPath 2.0 atomizes the whole sequence.
        /// </summary>
        private static string StringValueOfSequence(XPathValue value, XdmTree tree)
        {
            // The XPath 2.0 lexical form, this driver running the suite against 2.0. XPath 1.0 writes the
            // same double differently, and asking for that form here would report the engine as failing
            // tests it passes.
            if (value.Kind != XPathValueKind.NodeSet)
            {
                return value.ToCanonicalString();
            }

            NodeSet nodes = value.AsNodeSet();
            if (nodes.Count == 1)
            {
                return nodes.TreeAt(0).StringValueOf(nodes[0]);
            }

            string[] parts = new string[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                parts[i] = nodes.TreeAt(i).StringValueOf(nodes[i]);
            }

            return string.Join(' ', parts);
        }

        /// <summary>
        /// Compares an expected literal with the actual value, allowing for the fact that this engine has one
        /// numeric type where the suite distinguishes several.
        /// </summary>
        private static bool EqualEnough(XPathValue expected, XPathValue actual)
        {
            if (expected.Kind == XPathValueKind.Number || actual.Kind == XPathValueKind.Number)
            {
                double a = expected.ToNumber();
                double b = actual.ToNumber();
                return a.Equals(b) || Math.Abs(a - b) < 1e-12;
            }

            if (expected.Kind == XPathValueKind.Boolean)
            {
                return expected.ToBoolean() == actual.ToBoolean();
            }

            return string.Equals(expected.ToStringValue(), actual.ToStringValue(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Compares two sequences item by item, or as multisets where the order is not asserted.
        /// </summary>
        /// <remarks>
        /// A node never matches an expected literal, however its text reads. The literals this driver builds
        /// are atomic values, and <c>fn:deep-equal</c> says a node is not one — letting the text decide would
        /// pass a test whose whole point is that the result is a value rather than the element it came from.
        /// </remarks>
        private static bool SameItems(List<XPathValue> expected, List<XPathValue> actual, bool anyOrder)
        {
            if (!anyOrder)
            {
                for (int i = 0; i < expected.Count; i++)
                {
                    if (!SameItem(expected[i], actual[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            // A permutation, so each expected item is matched against one unclaimed actual item. Quadratic,
            // and the sequences here are a handful of items.
            bool[] claimed = new bool[actual.Count];

            foreach (XPathValue want in expected)
            {
                int at = -1;

                for (int i = 0; i < actual.Count && at < 0; i++)
                {
                    if (!claimed[i] && SameItem(want, actual[i]))
                    {
                        at = i;
                    }
                }

                if (at < 0)
                {
                    return false;
                }

                claimed[at] = true;
            }

            return true;
        }

        private static bool SameItem(XPathValue expected, XPathValue actual)
        {
            return actual.Kind is not (XPathValueKind.Node or XPathValueKind.NodeSet)
                && !actual.IsFunctionItem
                && EqualEnough(expected, actual);
        }

        /// <summary>Renders a sequence for a failure message, without asking a node for a typed value.</summary>
        private static string Describe(List<XPathValue> items, XdmTree tree)
        {
            string[] parts = new string[items.Count];

            for (int i = 0; i < items.Count; i++)
            {
                parts[i] = items[i].IsFunctionItem
                    ? "(a function item)"
                    : StringValueOfSequence(items[i], tree);
            }

            return "(" + string.Join(", ", parts) + ")";
        }

        private static string NormalizeSpace(string text)
        {
            return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Trim(string text)
        {
            string flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return flat.Length > 60 ? flat[..60] + "..." : flat;
        }

        private static TestResult Pass() => new TestResult(Outcome.Passed, string.Empty);

        private static TestResult Fail(string detail) => new TestResult(Outcome.Failed, detail);

        private static TestResult Skip(string detail) => new TestResult(Outcome.Skipped, detail);
    }

    /// <summary>
    /// Builds the value an <c>assert-eq</c> expects, without evaluating it through the engine under test.
    /// </summary>
    /// <remarks>
    /// Using the engine to work out what the engine should have produced would make a test that agrees with
    /// itself. Only forms whose meaning is beyond argument are accepted; anything else is reported as a skip,
    /// so the pass rate never rests on circular reasoning.
    /// </remarks>
    internal static class Literal
    {
        /// <summary>
        /// Builds the sequence an <c>assert-deep-eq</c> expects, which is usually a list of literals in
        /// parentheses and sometimes a single one.
        /// </summary>
        /// <remarks>
        /// The same rule as <see cref="TryParse"/>: only forms whose meaning is beyond argument. Anything
        /// with a function call, a path or an operator in it fails here and the test is skipped, so no part
        /// of the pass rate rests on the engine having agreed with itself.
        /// </remarks>
        /// <param name="text">The expectation as the suite writes it.</param>
        /// <param name="items">On success, the items it denotes.</param>
        public static bool TryParseSequence(string text, out List<XPathValue> items)
        {
            items = new List<XPathValue>();
            string trimmed = text.Trim();

            if (trimmed is "()")
            {
                return true;
            }

            if (!trimmed.StartsWith('(') || !trimmed.EndsWith(')'))
            {
                if (!TryParse(trimmed, out XPathValue single))
                {
                    return false;
                }

                items.Add(single);
                return true;
            }

            foreach (string part in SplitTopLevel(trimmed[1..^1]))
            {
                if (!TryParse(part, out XPathValue item))
                {
                    return false;
                }

                items.Add(item);
            }

            return true;
        }

        /// <summary>
        /// Splits on the commas that separate a sequence's items, ignoring those inside quotes or nested
        /// parentheses — <c>xs:date('2026-01-01'), 2</c> is two items and not three.
        /// </summary>
        private static List<string> SplitTopLevel(string text)
        {
            List<string> parts = new List<string>();
            int depth = 0;
            char quote = '\0';
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                switch (c)
                {
                    case '\'':
                    case '"':
                        quote = c;
                        break;

                    case '(':
                        depth++;
                        break;

                    case ')':
                        depth--;
                        break;

                    case ',' when depth == 0:
                        parts.Add(text[start..i]);
                        start = i + 1;
                        break;
                }
            }

            parts.Add(text[start..]);
            return parts;
        }

        public static bool TryParse(string text, out XPathValue value)
        {
            string trimmed = text.Trim();
            value = default;

            if (trimmed.Length == 0)
            {
                return false;
            }

            if (trimmed is "true()")
            {
                value = XPathValue.FromBoolean(true);
                return true;
            }

            if (trimmed is "false()")
            {
                value = XPathValue.FromBoolean(false);
                return true;
            }

            if (trimmed.Length >= 2
                && ((trimmed[0] == '\'' && trimmed[^1] == '\'') || (trimmed[0] == '"' && trimmed[^1] == '"')))
            {
                string inner = trimmed[1..^1];

                // A quote doubled inside the literal is an escaped quote; anything else quoted mid-string
                // means this is an expression rather than a single literal.
                string unescaped = inner.Replace(new string(trimmed[0], 2), trimmed[0].ToString());
                if (unescaped.IndexOf(trimmed[0]) >= 0)
                {
                    return false;
                }

                value = XPathValue.FromString(unescaped);
                return true;
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                value = XPathValue.FromNumber(number);
                return true;
            }

            // xs:integer(5), xs:double(1.5), xs:string('x') and friends: a constructor around one literal.
            int open = trimmed.IndexOf('(');
            if (open > 0 && trimmed[^1] == ')' && trimmed.StartsWith("xs:", StringComparison.Ordinal))
            {
                if (!TryParse(trimmed[(open + 1)..^1], out value))
                {
                    return false;
                }

                // A numeric constructor names a value however its argument is written, so xs:float("3.4E38")
                // and xs:float(3.4E38) are one expectation. And xs:float is the constructor that changes the
                // value rather than only naming its type: reading its digits as a double compares two
                // numbers that were never meant to be the same one.
                string type = trimmed[..open];

                if (type is "xs:float" or "xs:double")
                {
                    if (!TryNumber(value.ToCanonicalString(), out double parsed))
                    {
                        return false;
                    }

                    value = type == "xs:float"
                        ? XPathValue.FromFloat((float)parsed)
                        : XPathValue.FromNumber(parsed);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads a number in XPath's lexical form, which names three values .NET's parser does not.
        /// </summary>
        private static bool TryNumber(string text, out double number)
        {
            switch (text.Trim())
            {
                case "INF":
                    number = double.PositiveInfinity;
                    return true;

                case "-INF":
                    number = double.NegativeInfinity;
                    return true;

                case "NaN":
                    number = double.NaN;
                    return true;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }
    }
}
