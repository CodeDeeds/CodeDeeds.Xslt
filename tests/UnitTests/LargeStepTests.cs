using System.Reflection;
using System.Xml;
using System.Xml.Xsl;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for a step that holds a large part of a document at once — every node of it, or every
    /// descendant on the way to a positional predicate that <c>//x[P]</c> cannot fold away — for what it
    /// answers, which did not change, and what it allocates, which did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A path's working lists come from <c>NodeListPool</c>, which dropped any list whose capacity had
    /// grown past 4,096 rather than keep it. A step over a document of twenty-four thousand nodes grew its
    /// list from there to 32,768 at every call, a quarter of a megabyte of arrays each time, and the
    /// figure was taken for the cost of the positional predicate that happened to be beside it. A few
    /// large lists are kept now, bounded in number and in size.
    /// </para>
    /// <para>
    /// The answers are asked of both backends at 1.0 and 3.0, and for the 1.0 stylesheet of
    /// <c>XslCompiledTransform</c> too, since a list reused with the wrong contents would show up here
    /// first. See <c>ConformanceNotes.md</c>, "A list too large to keep".
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class LargeStepTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>A document large enough for a step over it to outgrow the pool's small limit.</summary>
        private static readonly string Source = BuildSource();

        private static string BuildSource()
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder("<r>");

            for (int group = 1; group <= 40; group++)
            {
                builder.Append("<g n=\"").Append(group).Append("\">");

                for (int item = 1; item <= 50; item++)
                {
                    builder.Append("<i n=\"").Append(item).Append("\"><a>").Append(group * 100 + item)
                        .Append("</a><b>x</b><c/></i>");
                }

                builder.Append("</g>");
            }

            return builder.Append("</r>").ToString();
        }

        private static string Stylesheet(string version, string body)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\"><xsl:output method=\"text\"/>"
                + $"<xsl:template match=\"/r\">{body}</xsl:template></xsl:stylesheet>";
        }

        /// <summary>The one answer both backends write for a template body, at one version.</summary>
        private static string Writes(string body, string version)
        {
            string stylesheet = Stylesheet(version, body);
            string? answer = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                string written = new Xslt(stylesheet, new XsltOptions { Backend = backend, Version = XsltVersion.V30 })
                    .TransformXml(Source);

                answer ??= written;
                Assert.AreEqual(answer, written, $"the backends disagree about: {body}");
            }

            return answer!;
        }

        /// <summary>What <c>XslCompiledTransform</c> writes for the same body as a 1.0 stylesheet.</summary>
        private static string TheOracleWrites(string body)
        {
            XslCompiledTransform reference = new XslCompiledTransform();
            using (XmlReader reader = XmlReader.Create(new StringReader(Stylesheet("1.0", body))))
            {
                reference.Load(reader);
            }

            StringWriter output = new StringWriter();
            using (XmlReader reader = XmlReader.Create(new StringReader(Source)))
            {
                reference.Transform(reader, null, output);
            }

            return output.ToString();
        }

        private static string Selecting(params string[] expressions)
        {
            return string.Join(
                "|",
                expressions.Select(expression =>
                    $"<xsl:value-of select=\"{expression.Replace("<", "&lt;", StringComparison.Ordinal)}\"/>"));
        }

        [TestMethod]
        public void APositionalPredicateOnADescendantStepCountsAmongSiblings()
        {
            // //x[P] with a P that reads the position is descendant-or-self::node()/child::x[P], so the
            // position is among each parent's children and not among every x in the document: the first
            // i of every group, forty of them, and not the first i of all.
            string body = Selecting(
                "count(//i[position() < 3])", "count(//i[1])", "count(//i[last()])", "count(//i[position() = last()])",
                "count(//i[position() > 48])", "count(//g[1]/i)", "count(//g[last()]/i[last()])",
                "count(//i[a > 500][1])", "count(//i[a > 500][position() < 3])",
                "//i[position() = 2][3]/a", "(//i)[2]/a", "(//i)[position() = last()]/a", "count((//i)[position() < 3])",
                "count(//i[position() mod 2 = 0])", "count(//*[position() = 1])", "count(//node()[position() = 1])",
                "count(//i/preceding-sibling::i[1])", "count(//i/preceding-sibling::i[position() < 3])",
                "//g[3]/i[2]/preceding-sibling::i[1]/a", "//g[3]/i[2]/following-sibling::i[last()]/a",
                "count(//i/ancestor::*[1])", "count(//c/ancestor-or-self::*[2])", "//g[2]/i[1]/c/ancestor::*[position() = 2]/@n",
                "count(//i[position() < 3][a > 2000])", "count(//i[count(../i[position() < 10]) = 9])");

            const string Expected =
                "80|40|40|40|80|50|1|36|72|"
                + "|102|4050|2|"
                + "1000|2042|6042|"
                + "1960|1960|"
                + "301|350|"
                + "40|2000|2|"
                + "42|2000";

            Assert.AreEqual(Expected, TheOracleWrites(body), "the oracle itself");

            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Assert.AreEqual(Expected, Writes(body, version), version);
            }
        }

        [TestMethod]
        public void AStepOverTheWholeDocumentAnswersAsBefore()
        {
            string body = Selecting(
                "count(//node())", "count(//*)", "count(//text())", "count(//i/*)", "count(//c)",
                "count(descendant::node())", "count(//node()/..)", "count(//*/@n)",
                "sum(//i/@n)", "count(//i[a mod 7 = 0])", "count(//i[a mod 7 = 0][position() < 5])");

            const string Expected = "12041|8041|4000|6000|2000|12040|6042|2040|51000|286|160";

            Assert.AreEqual(Expected, TheOracleWrites(body), "the oracle itself");

            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Assert.AreEqual(Expected, Writes(body, version), version);
            }
        }

        [TestMethod]
        public void OneTransformationRunsManyLargeStepsAndEachSeesItsOwnNodes()
        {
            // A large list kept and handed out again must come back empty and hold only what the next
            // step puts in it: a hundred steps over the whole document in one template, one after another
            // and one inside another, each answering for itself.
            const string Body =
                "<xsl:for-each select=\"//g[position() &lt; 6]\">"
                + "<xsl:value-of select=\"count(//i[position() = 1]) + count(//node()) - count(//i[a &gt; 0][position() &lt; 3])\"/>"
                + "<xsl:text>,</xsl:text>"
                + "<xsl:value-of select=\"count(//i[count(//c[position() = 1]) = 40][position() = last()])\"/>"
                + "<xsl:text>;</xsl:text></xsl:for-each>";

            const string Expected = "12001,0;12001,0;12001,0;12001,0;12001,0;";

            foreach (string version in new[] { "1.0", "3.0" })
            {
                Assert.AreEqual(Expected, Writes(Body, version), version);
            }
        }

        private static readonly Type Pool = typeof(Xslt).Assembly.GetType("CodeDeeds.Xslt.XPath.NodeListPool")!;

        private static readonly Func<List<int>> Rent =
            Pool.GetMethod("Rent", BindingFlags.Public | BindingFlags.Static)!.CreateDelegate<Func<List<int>>>();

        private static readonly Action<List<int>> Return =
            Pool.GetMethod("Return", BindingFlags.Public | BindingFlags.Static)!.CreateDelegate<Action<List<int>>>();

        /// <summary>Rents until the pool is empty, so that a test starts from nothing.</summary>
        private static List<List<int>> Drain()
        {
            List<List<int>> held = new List<List<int>>();
            List<int> last = Rent();

            // A fresh list is what the pool makes when it has nothing: sixteen slots and never larger.
            while (last.Capacity != 16 || held.Count == 0)
            {
                held.Add(last);
                last = Rent();

                if (held.Count > 64)
                {
                    break;
                }
            }

            return held;
        }

        [TestMethod]
        public void TheNodeListPoolKeepsAFewLargeListsAndNoMore()
        {
            // The pool is per thread, so this runs where nothing else is renting; what fails there is
            // failed here, an assertion on another thread being a crash of the host and not a verdict.
            Exception? failed = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    OnAFreshThread();
                }
                catch (Exception exception)
                {
                    failed = exception;
                }
            });

            thread.Start();
            thread.Join();

            if (failed is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failed);
            }
        }

        private static void OnAFreshThread()
        {
            Drain();

            // A list grown past the small limit comes back on the next rent, its capacity kept.
            List<int> large = Rent();
            large.AddRange(Enumerable.Range(0, 30000));
            int capacity = large.Capacity;
            Assert.IsGreaterThan(4096, capacity);
            Return(large);

            List<int> again = Rent();
            Assert.AreSame(large, again, "a large list was dropped rather than kept");
            Assert.AreEqual(capacity, again.Capacity);
            Assert.IsEmpty(again, "a kept list has to come back cleared");
            Return(again);

            // Four of them, and the fifth is let go.
            List<int>[] several = new List<int>[6];
            for (int i = 0; i < several.Length; i++)
            {
                several[i] = Rent();
                several[i].AddRange(Enumerable.Range(0, 20000));
            }

            for (int i = 0; i < several.Length; i++)
            {
                Return(several[i]);
            }

            int kept = 0;
            for (int i = 0; i < several.Length; i++)
            {
                List<int> rented = Rent();
                if (rented.Capacity > 4096)
                {
                    kept++;
                    Assert.IsGreaterThanOrEqualTo(0, Array.IndexOf(several, rented), "a large list came from nowhere");
                }
            }

            Assert.AreEqual(4, kept, "the pool keeps four large lists");

            // One past the largest size is dropped whatever the count.
            Drain();
            List<int> huge = Rent();
            huge.Capacity = (1 << 20) + 1;
            Return(huge);
            Assert.AreNotSame(huge, Rent(), "a list past the largest retained size was kept");
        }

        [TestMethod]
        public void AStepOverTheWholeDocumentAllocatesNothingOnceWarm()
        {
            // The document has ten thousand nodes, so //node() outgrows the small limit by a distance:
            // it was 150 kilobytes of arrays a call here and is none, the lists being kept.
            string stylesheet = Stylesheet("3.0", Selecting("count(//node())", "count(//i[position() < 3])"));

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                Xslt transform = new Xslt(stylesheet, new XsltOptions { Backend = backend, Version = XsltVersion.V30 });
                XdmTree tree = XdmTreeBuilder.FromXml(Source, fragment: false);

                for (int i = 0; i < 20; i++)
                {
                    transform.Transform(tree);
                }

                long best = long.MaxValue;
                for (int round = 0; round < 5; round++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    transform.Transform(tree);
                    best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
                }

                Assert.IsLessThan(8192, best, $"a transformation over the document allocates {best} bytes on the {backend} backend");
            }
        }
    }
}
