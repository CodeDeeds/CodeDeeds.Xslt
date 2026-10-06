using System.Runtime.CompilerServices;
using CodeDeeds.Xslt;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for four costs a transformation paid for every document, however small: a context copied with
    /// seven fields that never changed, a mapping of the stylesheet's names and an index of its templates
    /// built for every temporary tree, the strip-space declarations walked for every whitespace node, and
    /// every sequence laid out again in a list of its own to be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found in the DocBook xslTNG stylesheets, which took 20 milliseconds on a document of three hundred
    /// characters after the costs before these were out of the way: they build hundreds of temporary trees
    /// for any document at all, pass sequences of some forty elements from template to template with a type
    /// on every parameter, and declare two hundred names to strip whitespace in. See
    /// <c>ConformanceNotes.md</c>, "What every DocBook document paid, continued".
    /// </para>
    /// <para>
    /// Most of these hold answers, which were right before, and three measure.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class FixedCostTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" exclude-result-prefixes=\"#all\">";

        private sealed class Documents : IXsltResolver
        {
            private readonly Dictionary<string, string> m_documents = new(StringComparer.Ordinal);

            public Documents Add(string name, string text)
            {
                m_documents[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_documents.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static Xslt Compiled(string body, XsltBackend backend = XsltBackend.Interpreted, IXsltResolver? documents = null)
        {
            return new Xslt(
                Head + body + "</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Backend = backend,
                    DocumentResolver = documents,
                });
        }

        /// <summary>What a stylesheet writes of a document, on both backends, which have to agree.</summary>
        private static string Run(string body, string input = "<r/>", IXsltResolver? documents = null)
        {
            string interpreted = Compiled(body, XsltBackend.Interpreted, documents).TransformXml(input);
            string compiled = Compiled(body, XsltBackend.Compiled, documents).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "The backends disagree.");
            return interpreted;
        }

        private static XPathValue Evaluate(string xml, string expression)
        {
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader(xml));
            XPathStaticContext statics = new XPathStaticContext { Version = XsltVersion.V30 };
            Expr parsed = XPathParser.Parse(expression, statics);
            DynamicContext context = new DynamicContext(tree, XdmTree.RootNode, statics.Names.BuildFingerprintMap(tree), statics.Names);

            return parsed.Evaluate(ref context);
        }

        /// <summary>The least a transformation allocates in several runs, once the code is warm.</summary>
        private static long LeastAllocated(Xslt transformation, string input)
        {
            long least = long.MaxValue;

            for (int i = 0; i < 12; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                transformation.TransformXml(input, TextWriter.Null);
                least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            return least;
        }

        // ---- The context and its surroundings --------------------------------------------------------------

        [TestMethod]
        public void ACopyOfAContextSharesItsSurroundings()
        {
            // Every change of focus copies the context, and what the copy carries of the transformation is
            // one reference to the same surroundings, not seven fields copied over.
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader("<r><a/><b/></r>"));
            Surroundings given = new Surroundings { Globals = new[] { XPathValue.FromNumber(6) } };
            DynamicContext context = new DynamicContext(tree, XdmTree.RootNode, Array.Empty<int>(), given);

            DynamicContext moved = context.SwitchTree(tree, 1);
            DynamicContext atomic = context.WithAtomicItem(XPathValue.FromNumber(1));
            DynamicContext item = context.WithItem(XPathValue.FromNode(tree, 2));

            Assert.AreSame(given, moved.Surroundings);
            Assert.AreSame(given, atomic.Surroundings);
            Assert.AreSame(given, item.Surroundings);
            Assert.AreEqual(6.0, item.Globals[0].ToNumber());
            Assert.IsNull(item.Runtime);
        }

        [TestMethod]
        public void SettingOneThingGivesAContextSurroundingsOfItsOwn()
        {
            // A context given one thing more keeps what it had and leaves the context it was copied from
            // as it was: the surroundings are not changed once made, the copy gets new ones.
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader("<r/>"));
            Clock clock = new Clock();
            DynamicContext context = new DynamicContext(tree, XdmTree.RootNode, Array.Empty<int>(), new Surroundings { Clock = clock });
            Surroundings before = context.Surroundings;

            DynamicContext copy = context;
            copy.Globals = new[] { XPathValue.FromString("x") };

            Assert.AreSame(before, context.Surroundings);
            Assert.AreEqual(0, context.Globals.Length);
            Assert.AreNotSame(before, copy.Surroundings);
            Assert.AreEqual("x", copy.Globals[0].ToStringValue());
            Assert.AreSame(clock, copy.Clock, "What was not set is kept.");
            Assert.AreSame(clock, copy.ReadClock());
        }

        [TestMethod]
        public void AContextGivenNoSurroundingsHasEmptyOnes()
        {
            // The constructor that takes no surroundings gives the empty ones, shared, so that reading
            // through them never finds null; and a clock asked for where none was given is made once.
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader("<r/>"));
            DynamicContext context = new DynamicContext(tree, XdmTree.RootNode, Array.Empty<int>());

            Assert.AreSame(Surroundings.None, context.Surroundings);
            Assert.IsNull(context.Runtime);
            Assert.AreEqual(0, context.Globals.Length);

            Clock made = context.ReadClock();

            Assert.AreSame(made, context.ReadClock());
            Assert.AreNotSame(Surroundings.None, context.Surroundings, "The shared empty surroundings are not written to.");
            Assert.IsNull(Surroundings.None.Clock);
        }

        [TestMethod]
        public void TheContextIsNoWiderThanNinetySixBytes()
        {
            // The whole point: twelve fields on a 64-bit machine, where there were eighteen in 144 bytes,
            // and a copy of it is what every change of focus costs.
            if (IntPtr.Size != 8)
            {
                return;
            }

            DynamicContext a = default, b = default;
            long distance = Math.Abs((long)Unsafe.ByteOffset(ref a.Node, ref b.Node));

            Assert.IsTrue(distance <= 96, $"A context is {distance} bytes.");
        }

        // ---- One name table for the trees a transformation builds ------------------------------------------

        [TestMethod]
        public void AnElementFirstSeenInALaterTemporaryTreeIsMatchedByName()
        {
            // The mapping of the stylesheet's names for the shared table is made when the first temporary
            // tree is stepped into; 'late' is in no tree at that point, and comes in with a tree built
            // afterwards. A mapping made by looking names up would say the name does not exist, and the
            // template and the path below would both miss it.
            string body =
                "<xsl:template match=\"/\">"
                + "<xsl:variable name=\"first\"><a/></xsl:variable>"
                + "<xsl:variable name=\"n\" select=\"count($first/a)\"/>"
                + "<xsl:variable name=\"second\"><late><x/><x/></late></xsl:variable>"
                + "<out n=\"{$n}\" x=\"{count($second/late/x)}\"><xsl:apply-templates select=\"$second/late\"/></out>"
                + "</xsl:template>"
                + "<xsl:template match=\"late\">late matched</xsl:template>"
                + "<xsl:template match=\"*\">something else</xsl:template>";

            Assert.AreEqual("<out n=\"1\" x=\"2\">late matched</out>", Run(body));
        }

        [TestMethod]
        public void ADocumentReadAndATreeBuiltAreMatchedByOneRule()
        {
            // A document doc() reads is this transformation's own and interned in the same table as the
            // trees it builds, and a template applies to a node of either.
            Documents documents = new Documents().Add("d.xml", "<late kind=\"read\"/>");
            string body =
                "<xsl:template match=\"/\">"
                + "<xsl:variable name=\"built\"><late kind=\"built\"/></xsl:variable>"
                + "<out><xsl:apply-templates select=\"doc('d.xml')/late, $built/late\"/></out>"
                + "</xsl:template>"
                + "<xsl:template match=\"late\"><xsl:value-of select=\"@kind\"/>;</xsl:template>";

            Assert.AreEqual("<out>read;built;</out>", Run(body, documents: documents));
        }

        [TestMethod]
        public void ANameEvaluateGivesASlotIsFoundInATreeBuiltBefore()
        {
            // xsl:evaluate may test a name the stylesheet never wrote, and gives it a slot at run time; the
            // mapping made before that is short and is made again, with the name interned into the shared
            // table — where a tree built before the slot existed already has it.
            string body =
                "<xsl:template match=\"/\">"
                + "<xsl:variable name=\"t\"><zed/><zed/><yed/></xsl:variable>"
                + "<xsl:variable name=\"warm\" select=\"count($t/*)\"/>"
                + "<xsl:variable name=\"found\" as=\"item()*\"><xsl:evaluate xpath=\"'count($t//zed)'\" context-item=\"/\">"
                + "<xsl:with-param name=\"t\" select=\"$t\"/></xsl:evaluate></xsl:variable>"
                + "<out warm=\"{$warm}\" found=\"{$found}\"/>"
                + "</xsl:template>";

            Assert.AreEqual("<out warm=\"3\" found=\"2\"/>", Run(body));
        }

        [TestMethod]
        public void OneInputTreeServesSeveralTransformationsAtOnce()
        {
            // The input tree keeps the table it came with and each transformation builds its own trees in a
            // table of its own, so a caller may hand one tree to many transformations at once and each may
            // bring names into the mix that the others never see.
            XdmTree input = XdmTreeBuilder.FromXml(new StringReader("<r><i/><i/><i/></r>"));
            Xslt transformation = Compiled(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:for-each select=\"1 to 50\"><xsl:variable name=\"t\"><e n=\"{.}\"><x/></e></xsl:variable>"
                + "<xsl:apply-templates select=\"$t/e\"/></xsl:for-each>"
                + "<xsl:apply-templates select=\"r/i\"/></out></xsl:template>"
                + "<xsl:template match=\"e\"><xsl:value-of select=\"@n\"/>,</xsl:template>"
                + "<xsl:template match=\"i\">i</xsl:template>");

            string[] results = new string[16];
            Parallel.For(0, results.Length, i =>
            {
                StringWriter writer = new StringWriter();
                transformation.Transform(input, writer);
                results[i] = writer.ToString();
            });

            string expected = "<out>" + string.Concat(Enumerable.Range(1, 50).Select(n => n + ",")) + "iii</out>";

            foreach (string result in results)
            {
                Assert.AreEqual(expected, result);
            }
        }

        [TestMethod]
        public void ATemporaryTreeCostsNoMappingOfItsOwn()
        {
            // Five hundred names tested, so that a mapping is two kilobytes, and two hundred temporary
            // trees each stepped into and matched against. With a mapping and an index for each tree the
            // transformation was 1,205 kilobytes and 2.9 milliseconds; with them shared it is 590 and 0.2,
            // which is the trees themselves. The bound leaves room for code not yet compiled its best.
            System.Text.StringBuilder names = new System.Text.StringBuilder();
            for (int i = 0; i < 500; i++)
            {
                names.Append("<xsl:template match=\"n").Append(i).Append("\">n</xsl:template>");
            }

            Xslt building = Compiled(
                names
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:for-each select=\"1 to 200\"><xsl:variable name=\"t\"><n7><n8/></n7></xsl:variable>"
                + "<xsl:apply-templates select=\"$t/n7/n8\"/></xsl:for-each></out></xsl:template>");

            Assert.AreEqual("<out>" + new string('n', 200) + "</out>", building.TransformXml("<r/>"));

            long least = LeastAllocated(building, "<r/>");

            Assert.IsTrue(
                least < 800 * 1024,
                $"Two hundred temporary trees allocated {least / 1024:N0} KB; with a mapping and an index for each it was 1,205.");
        }

        // ---- Whitespace stripping decided once a name ---------------------------------------------------------

        [TestMethod]
        public void ADeclarationMadeAfterADecisionIsHonoured()
        {
            // The decisions are kept by name, and a declaration added afterwards starts them over.
            WhitespaceControl control = new WhitespaceControl();
            control.Declare(null, "*", strip: true);

            Assert.IsTrue(control.ShouldStrip("", "a"));
            Assert.IsTrue(control.ShouldStrip("", "a"));

            control.Declare("", "a", strip: false);

            Assert.IsFalse(control.ShouldStrip("", "a"));
            Assert.IsTrue(control.ShouldStrip("", "b"));
            Assert.IsFalse(WhitespaceControl.PreserveAll.ShouldStrip("", "a"));
        }

        [TestMethod]
        public void TheDecisionIsTheSameOnEveryThread()
        {
            // The declarations belong to a compiled stylesheet, which several transformations use at once,
            // and each asks and is answered the same.
            WhitespaceControl control = new WhitespaceControl();
            for (int i = 0; i < 200; i++)
            {
                control.Declare("urn:x", "e" + i, strip: i % 2 == 0, precedence: 1);
            }

            control.Declare(null, "*", strip: false);

            Parallel.For(0, 32, _ =>
            {
                for (int i = 0; i < 200; i++)
                {
                    Assert.AreEqual(i % 2 == 0, control.ShouldStrip("urn:x", "e" + i));
                    Assert.IsFalse(control.ShouldStrip("urn:y", "e" + i));
                }
            });
        }

        [TestMethod]
        public void StrippingIsByTheMostSpecificDeclarationStill()
        {
            // The answers that were right before: an exact name beats a wildcard, a later module beats an
            // earlier one, and the stripping is of whitespace-only text alone.
            string body =
                "<xsl:strip-space elements=\"*\"/><xsl:preserve-space elements=\"keep\"/>"
                + "<xsl:template match=\"/\"><out><xsl:copy-of select=\"r/*\"/></out></xsl:template>";

            Assert.AreEqual(
                "<out><a><b/></a><keep> <b/> </keep><c>x </c></out>",
                Run(body, "<r> <a> <b/> </a> <keep> <b/> </keep> <c>x </c> </r>"));
        }

        // ---- A sequence read without being laid out again ---------------------------------------------------

        [TestMethod]
        public void ASequenceIsReadAsItself()
        {
            // The list a sequence reads as is the sequence; a node-set reads as its nodes, made as asked
            // for; a single item is a list of one; nothing is a list of none.
            XPathValue sequence = Evaluate("<r/>", "(1, 'two', 3.0)");
            XPathValue nodes = Evaluate("<r><a/><b/><c/></r>", "/r/*");

            Assert.AreSame(sequence.AsSequence(), XdmSequence.Items(sequence));
            Assert.AreSame(nodes.AsNodeSet(), XdmSequence.Items(nodes));
            Assert.AreEqual(3, XdmSequence.Items(nodes).Count);
            Assert.AreEqual("b", Evaluate("<r><a/><b/><c/></r>", "name((/r/*)[2])").ToStringValue());
            Assert.AreEqual(2, XdmSequence.Items(nodes)[1].NodeId - XdmSequence.Items(nodes)[0].NodeId + 1, "The second node of the set is the second item.");
            Assert.AreEqual(1, XdmSequence.Items(XPathValue.FromNumber(5)).Count);
            Assert.AreEqual(0, XdmSequence.Items(Evaluate("<r/>", "()")).Count);
            Assert.AreEqual(0, XdmSequence.Items(Evaluate("<r/>", "/r/none")).Count);
        }

        [TestMethod]
        public void ASequenceGivenNestedValuesHoldsTheirItems()
        {
            // Nested sequences do not exist in the data model, and a sequence made over values that are
            // several items each holds the items: what lets it be read as itself.
            XPathValue inner = Evaluate("<r/>", "(1, 2)");
            XPathValue nodes = Evaluate("<r><a/><b/></r>", "/r/*");
            XdmSequence built = new XdmSequence(new[] { inner, XPathValue.FromString("x"), nodes });

            Assert.AreEqual(5, built.Count);
            Assert.AreEqual(1.0, built[0].ToNumber());
            Assert.AreEqual("x", built[2].ToStringValue());
            Assert.AreEqual(XPathValueKind.Node, built[4].Kind);
        }

        [TestMethod]
        [DataRow("let $s := (1, 2, 3) return (remove($s, 1), '|', $s)", "2 3 | 1 2 3")]
        [DataRow("let $s := (1, 2, 3) return (insert-before($s, 2, 9), '|', $s)", "1 9 2 3 | 1 2 3")]
        [DataRow("let $s := (1, 2, 3) return (reverse($s), '|', $s)", "3 2 1 | 1 2 3")]
        [DataRow("let $s := (1, 2, 3) return (subsequence($s, 2), '|', $s)", "2 3 | 1 2 3")]
        [DataRow("let $s := (1, 2, 3) return (tail($s), '|', $s)", "2 3 | 1 2 3")]
        [DataRow("let $s := (3, 1, 2) return (sort($s), '|', $s)", "1 2 3 | 3 1 2")]
        [DataRow("let $s := (1, 2, 3) return (count(random-number-generator()?permute($s)), '|', $s)", "3 | 1 2 3")]
        [DataRow("let $s := (1, 2, 3) return (for-each($s, function($x) { $x * 2 }), '|', $s)", "2 4 6 | 1 2 3")]
        public void AFunctionThatRearrangesASequenceLeavesItAsItWas(string expression, string expected)
        {
            // What a function reads may be the sequence's own storage, so the ones that reorder, remove or
            // insert work on a copy of their own and the variable reads as it did.
            Assert.AreEqual(expected, Evaluate("<r/>", "string-join((" + expression + ") ! string(.), ' ')").ToStringValue());
        }

        [TestMethod]
        [DataRow("let $n := /r/* return (reverse($n) ! name(), '|', $n ! name())", "c b a | a b c")]
        [DataRow("let $n := /r/* return (remove($n, 2) ! name(), '|', $n ! name())", "a c | a b c")]
        [DataRow("let $n := /r/* return (empty($n), exists($n), count($n), empty(()))", "false true 3 true")]
        public void ANodeSetReadsAsItsNodes(string expression, string expected)
        {
            Assert.AreEqual(expected, Evaluate("<r><a/><b/><c/></r>", "string-join((" + expression + ") ! string(.), ' ')").ToStringValue());
        }

        [TestMethod]
        public void ATypedParameterOfManyItemsIsCheckedWithoutALaidOutCopy()
        {
            // A sequence of two thousand items handed to a function with a type on its parameter, two
            // hundred times: checked by laying the items out that was two hundred lists of forty-eight
            // kilobytes. The sequence reads as itself now, and the bound leaves room for the output.
            Xslt building = Compiled(
                "<xsl:function name=\"f:head\" as=\"xs:integer\"><xsl:param name=\"s\" as=\"xs:integer*\"/>"
                + "<xsl:sequence select=\"$s[1]\"/></xsl:function>"
                + "<xsl:template match=\"/\"><xsl:variable name=\"s\" as=\"xs:integer*\" select=\"(1 to 2000) ! xs:integer(.)\"/>"
                + "<out><xsl:value-of select=\"sum((1 to 200) ! f:head($s))\"/></out></xsl:template>");

            Assert.AreEqual("<out>200</out>", building.TransformXml("<r/>"));

            long least = LeastAllocated(building, "<r/>");

            Assert.IsTrue(
                least < 1024 * 1024,
                $"Two hundred calls over two thousand items allocated {least / 1024:N0} KB; laid out for each check, they were some ten megabytes.");
        }
    }
}
