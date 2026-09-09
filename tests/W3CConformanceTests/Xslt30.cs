using System.Text;
using System.Xml.Linq;

namespace CodeDeeds.Xslt.Conformance
{
    /// <summary>
    /// Runs the part of the W3C XSLT 3.0 suite that applies to this engine and reports where it stands.
    /// </summary>
    /// <remarks>
    /// The same shape of report as the QT3 run beside it, and for the same reason: what is wanted from a
    /// first run is not a list of thousands of failures but the handful of missing pieces behind them. The
    /// two numbers measure different halves of the language and are not comparable with each other.
    /// </remarks>
    internal static class Xslt30
    {
        /// <summary>
        /// The test sets to run, named in the XSLT30SETS environment variable as comma-separated parts of
        /// their names ("fn/base-uri,attr/mode"), or null to run them all.
        /// </summary>
        private static readonly string[]? s_sets = System.Environment.GetEnvironmentVariable("XSLT30SETS")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>Whether the tests listed in full are the skipped ones rather than the failed ones.</summary>
        public static bool ListSkips { get; set; }

        public static int Run(string[] args, XsltVersion version, XsltBackend backend)
        {
            string? root = Locate(args.Length > 0 ? args[0] : null);

            if (root is null)
            {
                Console.Error.WriteLine(
                    "The XSLT 3.0 test suite was not found. It is 460 MB of W3C test data and is deliberately");
                Console.Error.WriteLine("not part of this repository. Clone it and point this at it:");
                Console.Error.WriteLine();
                Console.Error.WriteLine("  git -c core.longpaths=true clone --depth 1 https://github.com/w3c/xslt30-test.git");
                Console.Error.WriteLine("  dotnet run --project CodeDeeds.Xslt.Conformance -- --xslt <path-to-xslt30-test>");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Or set XSLT30TESTS to its path. A second argument filters which failures");
                Console.Error.WriteLine("are listed in full, which is how you look into one test set at a time.");
                return 1;
            }

            Xslt30Catalog catalog = Xslt30Catalog.Load(root);
            Xslt30Runner runner = new Xslt30Runner(catalog, version, backend);

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
                string path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    continue;
                }

                string[] parts = file.Split('/');

                // "tests/insn/choose/_choose-test-set.xml": the area is what kind of thing is being tested
                // and the set is which one of them, so the two together name a next task.
                string area = parts.Length > 1 ? parts[1] : "(root)";
                string name = parts.Length > 2 ? $"{parts[1]}/{parts[2]}" : area;

                // XSLT30SETS names the test sets to run, for iterating on one cluster without the whole run.
                if (s_sets is not null
                    && !Array.Exists(s_sets, wanted => name.Contains(wanted, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // With the file's identity kept, so that a source document written into the catalogue can
                // be told where it was written: an inline document has no file of its own, and its base
                // URI is the document it stands in.
                XElement testSet = XDocument.Load(path, LoadOptions.SetBaseUri).Root!;
                testSets++;

                string directory = Path.GetDirectoryName(path)!;

                foreach (XElement testCase in testSet.Elements(Xslt30Catalog.Ns + "test-case"))
                {
                    TestResult result;

                    try
                    {
                        result = runner.Run(testCase, testSet, directory);
                    }
                    catch (Exception exception)
                    {
                        result = new TestResult(
                            Outcome.Failed, $"driver fault: {exception.GetType().Name}: {exception.Message}");
                    }

                    switch (result.Outcome)
                    {
                        case Outcome.Passed:
                            passed++;
                            Program.Tally(byArea, area, true);
                            break;

                        case Outcome.Failed:
                            failed++;
                            Program.Tally(byArea, area, false);
                            Program.Bump(failureReasons, Category(result.Detail));
                            Program.Bump(byTestSet, name);

                            string line = $"{(string?)testCase.Attribute("name")} [{name}]: {result.Detail}";

                            bool wanted = args.Length < 2
                                || line.Contains(args[1], StringComparison.OrdinalIgnoreCase);

                            if (wanted && !ListSkips && examples.Count < 4000)
                            {
                                examples.Add(line);
                            }

                            break;

                        default:
                            skipped++;
                            Program.Bump(skipReasons, Reason(result.Detail));

                            string skip = $"{(string?)testCase.Attribute("name")} [{name}]: {result.Detail}";

                            if (ListSkips
                                && (args.Length < 2 || skip.Contains(args[1], StringComparison.OrdinalIgnoreCase))
                                && examples.Count < 4000)
                            {
                                examples.Add(skip);
                            }

                            break;
                    }
                }
            }

            int run = passed + failed;

            // The backend is named in the headline rather than left to whoever kept the output, because two
            // runs of this suite now differ by something the numbers alone do not say.
            Console.WriteLine($"=== xslt30-test, XSLT {version}-applicable subset, {backend} backend ===");
            Console.WriteLine();
            Console.WriteLine($"  test-sets read      {testSets}");
            Console.WriteLine($"  test-cases seen     {run + skipped:N0}");
            Console.WriteLine($"  skipped             {skipped:N0}");
            Console.WriteLine($"  run                 {run:N0}");
            Console.WriteLine($"    passed            {passed:N0}   ({(run == 0 ? 0 : 100.0 * passed / run):F1}%)");
            Console.WriteLine($"    failed            {failed:N0}");
            Console.WriteLine();

            Program.Report("Why tests were skipped", skipReasons, 16);
            Program.Report("What the failures are", failureReasons, 20);

            Console.WriteLine("Pass rate by area (areas with at least 20 tests run):");
            foreach ((string area, (int areaPassed, int total)) in byArea
                .Where(entry => entry.Value.Total >= 20)
                .OrderByDescending(entry => (double)entry.Value.Passed / entry.Value.Total))
            {
                Console.WriteLine($"  {area,-12}{areaPassed,6:N0} / {total,-6:N0}  {100.0 * areaPassed / total,5:F1}%");
            }

            Console.WriteLine();
            Program.Report("Where the failures are, by test set", byTestSet, 25);

            Console.WriteLine("A few failures in full:");
            foreach (string example in examples.Take(args.Length < 2 ? 15 : 4000))
            {
                Console.WriteLine($"  {example}");
            }

            return 0;
        }

        /// <summary>
        /// Groups a skip by its reason.
        /// </summary>
        /// <remarks>
        /// Kept whole, because a skip reason is the driver's own sentence and already says one thing. The
        /// name inside it is the part worth reading — which feature, which dependency, which error code was
        /// wanted — so this is deliberately not the collapsing the failures get.
        /// </remarks>
        private static string Reason(string detail)
        {
            string flat = detail.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return flat.Length > 90 ? flat[..90] : flat;
        }

        /// <summary>
        /// Reduces a failure to the kind of thing it is, so that a hundred failures naming a hundred
        /// different files collapse into one line naming what went wrong with them.
        /// </summary>
        private static string Category(string detail)
        {
            string flat = detail.Replace('\n', ' ').Replace('\r', ' ').Trim();

            foreach (string cut in new[] { " at offset", " at position" })
            {
                int at = flat.IndexOf(cut, StringComparison.Ordinal);
                if (at > 0)
                {
                    flat = flat[..at];
                }
            }

            // What is inside the quotes is which test this was; what is outside is what happened.
            StringBuilder builder = new StringBuilder(flat.Length);
            bool quoted = false;

            foreach (char character in flat)
            {
                if (character == '\'')
                {
                    if (!quoted)
                    {
                        builder.Append("'…'");
                    }

                    quoted = !quoted;
                    continue;
                }

                if (!quoted)
                {
                    builder.Append(character);
                }
            }

            string category = builder.ToString();
            return category.Length > 100 ? category[..100] : category;
        }

        /// <summary>
        /// Finds the suite: where the caller said, else where the environment says, else beside the
        /// repository, which is where it lands if it is cloned next to it.
        /// </summary>
        private static string? Locate(string? supplied)
        {
            IEnumerable<string> candidates = new[]
            {
                supplied,
                System.Environment.GetEnvironmentVariable("XSLT30TESTS"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "xslt30-test"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", "xslt30-test"),
            }.OfType<string>();

            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                if (File.Exists(Path.Combine(full, "catalog.xml"))
                    && Directory.Exists(Path.Combine(full, "tests")))
                {
                    return full;
                }
            }

            return null;
        }
    }
}
