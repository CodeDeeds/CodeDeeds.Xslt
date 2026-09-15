using System.Xml.Linq;
using CodeDeeds.Xslt;

namespace CodeDeeds.Xslt.Conformance
{
    /// <summary>
    /// Runs the part of the W3C QT3 test suite that applies to this engine and reports where it stands.
    /// </summary>
    /// <remarks>
    /// The point is a number that means something, so a test this driver cannot present fairly is skipped
    /// with a reason rather than counted either way. The failure summary is grouped, because what is wanted
    /// from a first run is not a list of 5,000 failures but the half-dozen missing pieces behind them.
    /// XPath 2.0 is what it runs by default, that being the version the engine claims; <c>--31</c> takes in
    /// the 3.0 and 3.1 tests as well, which is how the work towards those is measured.
    /// </remarks>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // The suite covers three versions of XPath and this engine claims one of them. Running the 3.0
            // and 3.1 tests is opt-in so that the headline figure stays comparable run to run, and so that
            // what is measured is always stated rather than inferred from the number.
            bool thirty = Array.Exists(args, argument => argument is "--30" or "--31" or "--xpath30");

            // The other suite entirely: QT3 measures expressions, xslt30-test measures the language around
            // them. Separate runs against separate clones, and neither number stands in for the other.
            bool xslt = Array.Exists(args, argument => argument is "--xslt" or "--xslt30");

            // Listing the skipped tests in full rather than the failed ones, filtered the same way, which is
            // how a test that stopped running is found: nothing else names it.
            Xslt30.ListSkips = Array.Exists(args, argument => argument == "--skips");

            // Which backend evaluates the expressions. The emitted one answers to the same suite as the
            // interpreter and must reach the same verdicts: an expression it cannot emit calls back into the
            // interpreted node it was compiled from, so a test that passes one way and fails the other is a
            // fault in what it did emit. Nothing else exercises the emitted code at this scale.
            XsltBackend backend = Array.Exists(args, argument => argument == "--compiled")
                ? XsltBackend.Compiled
                : XsltBackend.Interpreted;

            // Schema awareness is opt-in for the XSLT run while it is being built: on, the schemas an
            // environment declares are in scope and the tests marked schema_aware are judged rather than
            // skipped, which is how the work is measured without moving the headline figure under it.
            bool schemaAware = Array.Exists(args, argument => argument == "--schema");

            args = Array.FindAll(args, argument => !argument.StartsWith("--", StringComparison.Ordinal));

            XsltVersion version = thirty ? XsltVersion.V30 : XsltVersion.V20;

            if (xslt)
            {
                return Xslt30.Run(args, version, backend, schemaAware);
            }

            if (backend == XsltBackend.Compiled)
            {
                // QT3 parses and evaluates an expression directly rather than through a stylesheet, so there
                // is no XsltOptions in its path to carry a backend. Saying so beats running the interpreter
                // and reporting the number as though it had measured the other one.
                Console.Error.WriteLine(
                    "--compiled applies to the XSLT suite only: the QT3 runner evaluates expressions directly");
                Console.Error.WriteLine("rather than through a stylesheet, so no backend is selected there.");
                return 1;
            }

            string? root = LocateSuite(args.Length > 0 ? args[0] : null);

            if (root is null)
            {
                Console.Error.WriteLine(
                    "The QT3 test suite was not found. It is 77 MB of W3C test data and is deliberately not");
                Console.Error.WriteLine("part of this repository. Clone it and point this at it:");
                Console.Error.WriteLine();
                Console.Error.WriteLine("  git clone --depth 1 https://github.com/w3c/qt3tests.git");
                Console.Error.WriteLine("  dotnet run --project CodeDeeds.Xslt.Conformance -- <path-to-qt3tests>");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Or set QT3TESTS to its path. A second argument filters which failures");
                Console.Error.WriteLine("are listed in full, which is how you look into one category at a time.");
                return 1;
            }

            Catalog catalog = Catalog.Load(root);
            Runner runner = new Runner(catalog, version);

            int passed = 0;
            int failed = 0;
            int skipped = 0;
            int testSets = 0;

            Dictionary<string, int> failureReasons = new(StringComparer.Ordinal);
            Dictionary<string, int> skipReasons = new(StringComparer.Ordinal);
            Dictionary<string, (int Passed, int Total)> byArea = new(StringComparer.Ordinal);
            Dictionary<string, int> byTestSet = new(StringComparer.Ordinal);
            List<string> examples = new();

            foreach (string file in catalog.TestSetFiles)
            {
                string path = Path.Combine(root, file);
                if (!File.Exists(path))
                {
                    continue;
                }

                XElement testSet = XDocument.Load(path).Root!;
                testSets++;

                string area = file.Contains('/') ? file[..file.IndexOf('/')] : "(root)";

                foreach (XElement testCase in testSet.Elements(Catalog.Ns + "test-case"))
                {
                    TestResult result;

                    try
                    {
                        result = runner.Run(testCase, testSet, Path.GetDirectoryName(file));
                    }
                    catch (Exception exception)
                    {
                        result = new TestResult(Outcome.Failed, $"driver fault: {exception.GetType().Name}");
                    }

                    switch (result.Outcome)
                    {
                        case Outcome.Passed:
                            passed++;
                            Tally(byArea, area, true);
                            break;

                        case Outcome.Failed:
                            failed++;
                            Tally(byArea, area, false);
                            Bump(failureReasons, Categorize(result.Detail));
                            Bump(byTestSet, file);

                            string line = $"{(string?)testCase.Attribute("name")} [{file}]: {result.Detail}";

                            // Matched against the whole line rather than the detail alone, so that a test
                            // set can be named: "matches.re" is the question far more often than any
                            // particular wording of what went wrong in it.
                            bool wanted = args.Length < 2
                                || line.Contains(args[1], StringComparison.OrdinalIgnoreCase);

                            if (wanted && examples.Count < 2000)
                            {
                                examples.Add(line);
                            }

                            break;

                        default:
                            skipped++;
                            Bump(skipReasons, Categorize(result.Detail));
                            break;
                    }
                }
            }

            int run = passed + failed;

            Console.WriteLine($"=== QT3, XPath {version}-applicable subset ===");
            Console.WriteLine();
            Console.WriteLine($"  test-sets read      {testSets}");
            Console.WriteLine($"  test-cases seen     {run + skipped:N0}");
            Console.WriteLine($"  skipped             {skipped:N0}");
            Console.WriteLine($"  run                 {run:N0}");
            Console.WriteLine($"    passed            {passed:N0}   ({(run == 0 ? 0 : 100.0 * passed / run):F1}%)");
            Console.WriteLine($"    failed            {failed:N0}");
            Console.WriteLine();

            Report("Why tests were skipped", skipReasons, 12);
            Report("What the failures are", failureReasons, 20);

            Console.WriteLine("Pass rate by area (areas with at least 20 tests run):");
            foreach ((string area, (int areaPassed, int total)) in byArea
                .Where(entry => entry.Value.Total >= 20)
                .OrderByDescending(entry => (double)entry.Value.Passed / entry.Value.Total))
            {
                Console.WriteLine($"  {area,-12}{areaPassed,6:N0} / {total,-6:N0}  {100.0 * areaPassed / total,5:F1}%");
            }

            // Which file the failures are in is the question that decides what to work on next, and it is not
            // the same question as what kind of failure they are: one absent function shows up as a dozen
            // unrelated-looking reasons, while a reason shared across twenty files is nobody's next task.
            Report("Where the failures are, by test set", byTestSet, 20);

            Console.WriteLine();
            Console.WriteLine("A few failures in full:");
            foreach (string example in examples.Take(args.Length < 2 ? 15 : 2000))
            {
                Console.WriteLine($"  {example}");
            }

            return 0;
        }

        /// <summary>
        /// Finds the test suite: where the caller said, else where the environment says, else beside the
        /// repository, which is where it lands if it is cloned next to it.
        /// </summary>
        private static string? LocateSuite(string? supplied)
        {
            IEnumerable<string> candidates = new[]
            {
                supplied,
                System.Environment.GetEnvironmentVariable("QT3TESTS"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "qt3tests"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", "qt3tests"),
            }.OfType<string>();

            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                if (File.Exists(Path.Combine(full, "catalog.xml")))
                {
                    return full;
                }
            }

            return null;
        }

        internal static void Report(string title, Dictionary<string, int> reasons, int limit)
        {
            Console.WriteLine($"{title}:");

            foreach ((string reason, int count) in reasons.OrderByDescending(entry => entry.Value).Take(limit))
            {
                Console.WriteLine($"  {count,6:N0}  {reason}");
            }

            int rest = reasons.Count - limit;
            if (rest > 0)
            {
                Console.WriteLine($"         ... and {rest} other kinds");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Reduces a message to the thing it is about, so that a thousand failures naming a thousand different
        /// function arguments collapse into one line naming the function.
        /// </summary>
        internal static string Categorize(string detail)
        {
            foreach ((string prefix, string label) in new[]
            {
                ("Unknown function", "Unknown function"),
                ("error: Unknown function", "Unknown function"),
            })
            {
                int at = detail.IndexOf(prefix, StringComparison.Ordinal);
                if (at >= 0)
                {
                    int open = detail.IndexOf('\'', at);
                    int close = open < 0 ? -1 : detail.IndexOf('\'', open + 1);
                    if (close > open)
                    {
                        return $"{label} {detail[open..(close + 1)]}";
                    }
                }
            }

            // Otherwise keep the leading sentence, which is where the engine says what it objected to.
            string flat = detail.Replace('\n', ' ').Replace('\r', ' ').Trim();

            foreach (string cut in new[] { " at position", " in '", ": '", " '" })
            {
                int at = flat.IndexOf(cut, StringComparison.Ordinal);
                if (at > 0)
                {
                    flat = flat[..at];
                }
            }

            return flat.Length > 80 ? flat[..80] : flat;
        }

        internal static void Bump(Dictionary<string, int> counts, string key)
        {
            counts[key] = counts.TryGetValue(key, out int existing) ? existing + 1 : 1;
        }

        internal static void Tally(Dictionary<string, (int Passed, int Total)> areas, string area, bool passed)
        {
            (int p, int t) = areas.TryGetValue(area, out (int, int) existing) ? existing : (0, 0);
            areas[area] = (p + (passed ? 1 : 0), t + 1);
        }
    }
}
