namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:evaluate</c>: an XPath expression that arrives as a string while the transformation
    /// runs, compiled then against a context assembled from what the specification says it may see.
    /// </summary>
    /// <remarks>
    /// The interesting questions are all about the context: which namespaces, which variables, which
    /// functions, which focus. What the expression then computes is ordinary XPath, and is not what these
    /// tests are about.
    /// </remarks>
    [TestClass]
    public sealed class EvaluateTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" xmlns:p=\"urn:p\""
            + " xmlns:math=\"http://www.w3.org/2005/xpath-functions/math\""
            + " exclude-result-prefixes=\"xs f p math\">";

        private static string Run(string body, string input = "<r><a x='1'/><b/></r>", XsltOptions? options = null)
        {
            options ??= new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 };

            Xslt xslt = new Xslt(Head + body + "</xsl:stylesheet>", options);
            return input.Length == 0 ? xslt.Transform() : xslt.TransformXml(input);
        }

        private static string Root(string content)
        {
            return "<xsl:template match=\"/\"><out>" + content + "</out></xsl:template>";
        }

        private static string Refuses(string body, string input = "<r><a x='1'/><b/></r>", XsltOptions? options = null)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, input, options)).Code ?? string.Empty;
        }

        [TestMethod]
        public void ATargetExpressionIsCompiledWhenItArrives()
        {
            Assert.AreEqual("<out>3</out>", Run(Root("<xsl:evaluate xpath=\"'1 + 2'\"/>")));

            // Built at run time out of pieces, which is the point of the instruction.
            Assert.AreEqual(
                "<out>6</out>",
                Run(Root("<xsl:variable name=\"op\" select=\"'*'\"/><xsl:evaluate xpath=\"'2 ' || $op || ' 3'\"/>")));

            // Read out of the document, as an attribute node: converted to a string like any argument.
            Assert.AreEqual(
                "<out>7</out>",
                Run(Root("<xsl:evaluate xpath=\"/r/@e\"/>"), "<r e='3 + 4'/>"));
        }

        [TestMethod]
        public void TheParametersAreTheOnlyVariablesAndTheMapWins()
        {
            const string Sum = "<xsl:variable name=\"e\" select=\"'$p1 + $p2'\"/>";

            Assert.AreEqual(
                "<out>13</out>",
                Run(Root(Sum + "<xsl:evaluate xpath=\"$e\"><xsl:with-param name=\"p1\" select=\"6\"/>"
                    + "<xsl:with-param name=\"p2\" select=\"7\"/></xsl:evaluate>")));
            Assert.AreEqual(
                "<out>13</out>",
                Run(Root(Sum + "<xsl:evaluate xpath=\"$e\" with-params=\"map{xs:QName('p2'): 7}\">"
                    + "<xsl:with-param name=\"p1\" select=\"6\"/></xsl:evaluate>")));

            // A name in both takes the map's value.
            Assert.AreEqual(
                "<out>49</out>",
                Run(Root(Sum + "<xsl:evaluate xpath=\"$e\" with-params=\"map{xs:QName('p1'): 42, xs:QName('p2'): 7}\">"
                    + "<xsl:with-param name=\"p1\" select=\"6\"/></xsl:evaluate>")));

            // The stylesheet's own variables are not in scope, however near they stand.
            Assert.AreEqual("XPST0008", Refuses(Root(Sum + "<xsl:evaluate xpath=\"$e\"/>")));

            // And a with-param's as is checked where the value is made.
            Assert.AreEqual(
                "XTTE0590",
                Refuses(Root("<xsl:evaluate xpath=\"'$p'\"><xsl:with-param name=\"p\" as=\"xs:integer\" select=\"'x'\"/></xsl:evaluate>")));
        }

        [TestMethod]
        public void AWithParamsThatIsNotOneMapFromQNamesIsRefused()
        {
            Assert.AreEqual(
                "XTTE3165",
                Refuses(Root("<xsl:evaluate xpath=\"'$beast'\" with-params=\"map{'beast': 666}\"/>")));
            Assert.AreEqual(
                "XTTE3165",
                Refuses(Root("<xsl:evaluate xpath=\"'1'\" with-params=\"1\"/>")));
        }

        [TestMethod]
        public void TheFocusIsWhatContextItemSaysOrNothing()
        {
            Assert.AreEqual(
                "<out>1</out>",
                Run(Root("<xsl:evaluate xpath=\"'string(@x)'\" context-item=\"/r/a\"/>")));
            Assert.AreEqual(
                "<out>3</out>",
                Run(Root("<xsl:evaluate xpath=\"'string-length(.)'\" context-item=\"'abc'\"/>")));
            Assert.AreEqual(
                "<out>1 1</out>",
                Run(Root("<xsl:evaluate xpath=\"'position(), last()'\" context-item=\"/r/b\"/>")));

            // No context-item, or one selecting nothing, is no focus at all — not the instruction's.
            Assert.AreEqual("XPDY0002", Refuses(Root("<xsl:evaluate xpath=\"'@x'\"/>")));
            Assert.AreEqual("XPDY0002", Refuses(Root("<xsl:evaluate xpath=\"'@x'\" context-item=\"()\"/>")));
            Assert.AreEqual("XPDY0002", Refuses(Root("<xsl:evaluate xpath=\"'position()'\"/>")));

            // A focus is one item.
            Assert.AreEqual("XTTE3210", Refuses(Root("<xsl:evaluate xpath=\"'@x'\" context-item=\"/r/*\"/>")));
        }

        [TestMethod]
        public void NamespacesComeFromTheInstructionOrFromANamedNode()
        {
            const string Input = "<r xmlns:q='urn:p'><q:a>x</q:a></r>";

            // The prefixes in scope on the instruction.
            Assert.AreEqual(
                "<out>x</out>",
                Run(Root("<xsl:evaluate xpath=\"'/r/p:a/string()'\" context-item=\"/\"/>"), Input));

            // Or the ones in scope on the node named, which are the document's own.
            Assert.AreEqual(
                "<out>x</out>",
                Run(Root("<xsl:evaluate xpath=\"'/r/q:a/string()'\" context-item=\"/\" namespace-context=\"/r\"/>"), Input));
            Assert.AreEqual(
                "XPST0081",
                Refuses(Root("<xsl:evaluate xpath=\"'/r/p:a/string()'\" context-item=\"/\" namespace-context=\"/r\"/>"), Input));
            Assert.AreEqual(
                "XTTE3170",
                Refuses(Root("<xsl:evaluate xpath=\"'1'\" namespace-context=\"1\"/>"), Input));
        }

        [TestMethod]
        public void TheDefaultElementNamespaceIsXPathsAndNotTheStylesheets()
        {
            const string Input = "<r xmlns='urn:d'><a>y</a></r>";

            // The default namespace on the literal result elements around the instruction is not the
            // expression's, which takes its default from xpath-default-namespace alone.
            Assert.AreEqual(
                "<out xmlns=\"urn:d\"/>",
                Run("<xsl:template match=\"/\"><out xmlns=\"urn:d\"><xsl:evaluate xpath=\"'/r/a/string()'\" context-item=\"/\"/></out></xsl:template>", Input));
            Assert.AreEqual(
                "<out>y</out>",
                Run(Root("<xsl:evaluate xpath=\"'/r/a/string()'\" context-item=\"/\" xpath-default-namespace=\"urn:d\"/>"), Input));

            // A namespace-context node brings its own default namespace with it.
            Assert.AreEqual(
                "<out>y</out>",
                Run(Root("<xsl:evaluate xpath=\"'/r/a/string()'\" context-item=\"/\" namespace-context=\"/*\"/>"), Input));
        }

        [TestMethod]
        public void TheFunctionsAreXPathsAndTheStylesheetsPublicOnes()
        {
            const string Square =
                "<xsl:function name=\"f:sq\" as=\"xs:integer\" visibility=\"public\"><xsl:param name=\"x\" as=\"xs:integer\"/>"
                + "<xsl:sequence select=\"$x * $x\"/></xsl:function>";
            const string Hidden =
                "<xsl:function name=\"f:sq\" as=\"xs:integer\"><xsl:param name=\"x\" as=\"xs:integer\"/>"
                + "<xsl:sequence select=\"$x * $x\"/></xsl:function>";

            Assert.AreEqual("<out>25</out>", Run(Square + Root("<xsl:evaluate xpath=\"'f:sq(5)'\"/>")));
            Assert.AreEqual("<out>25</out>", Run(Square + Root("<xsl:evaluate xpath=\"'Q{urn:f}sq(5)'\"/>")));
            Assert.AreEqual("XTDE3160", Refuses(Hidden + Root("<xsl:evaluate xpath=\"'f:sq(5)'\"/>")));
            Assert.AreEqual("XTDE3160", Refuses(Square + Root("<xsl:evaluate xpath=\"'f:sq(5, 6)'\"/>")));

            // The XPath library, the math functions and the constructors are there.
            Assert.AreEqual(
                "<out>ab 8 3</out>",
                Run(Root("<xsl:evaluate xpath=\"'concat(&quot;a&quot;, &quot;b&quot;), math:pow(2, 3), xs:integer(&quot;3&quot;)'\"/>")));

            // What XSLT adds to the function namespace is not, by the specification's own list.
            Assert.AreEqual("XTDE3160", Refuses(Root("<xsl:evaluate xpath=\"'current()'\" context-item=\"/\"/>")));
            Assert.AreEqual("XTDE3160", Refuses(Root("<xsl:evaluate xpath=\"'document(&quot;x.xml&quot;)'\"/>")));
            Assert.AreEqual("XTDE3160", Refuses(Root("<xsl:evaluate xpath=\"'key(&quot;k&quot;, 1)'\" context-item=\"/\"/>")));
            Assert.AreEqual("XTDE3160", Refuses(Root("<xsl:evaluate xpath=\"'system-property(&quot;xsl:version&quot;)'\"/>")));
        }

        [TestMethod]
        public void TheDecimalFormatsAreTheStylesheets()
        {
            const string Format = "<xsl:decimal-format name=\"p:f\" decimal-separator=\",\" grouping-separator=\".\"/>";

            Assert.AreEqual(
                "<out>1,5</out>",
                Run(Format + Root("<xsl:evaluate xpath=\"'format-number(1.5, &quot;#,0&quot;, &quot;p:f&quot;)'\"/>")));
            Assert.AreEqual(
                "FODF1280",
                Refuses(Format + Root("<xsl:evaluate xpath=\"'format-number(1.5, &quot;#.0&quot;, &quot;p:nonesuch&quot;)'\"/>")));
        }

        [TestMethod]
        public void TheResultIsHeldToTheTypeDeclared()
        {
            Assert.AreEqual(
                "<out>1</out>",
                Run(Root("<xsl:evaluate xpath=\"'/r/a/@x'\" as=\"xs:string\" context-item=\"/\"/>")));
            Assert.AreEqual("XPTY0004", Refuses(Root("<xsl:evaluate xpath=\"'2 + 2'\" as=\"xs:string\"/>")));
        }

        [TestMethod]
        public void AStaticErrorInTheTargetIsADynamicOne()
        {
            Assert.AreEqual("XTDE3160", Refuses(Root("<xsl:evaluate xpath=\"'1 +'\"/>")));
            Assert.AreEqual("XTDE3160", Refuses(Root("<xsl:evaluate xpath=\"'nonesuch()'\"/>")));

            // Which an xsl:try can catch, being dynamic.
            Assert.AreEqual(
                "<out>caught</out>",
                Run(Root("<xsl:try><xsl:evaluate xpath=\"'1 +'\"/><xsl:catch errors=\"err:XTDE3160\" select=\"'caught'\""
                    + " xmlns:err=\"http://www.w3.org/2005/xqt-errors\"/></xsl:try>")));
        }

        [TestMethod]
        public void TheBaseUriIsTheInstructionsUnlessSaid()
        {
            Assert.AreEqual(
                "<out>http://example.com/base/x</out>",
                Run(Root("<xsl:evaluate xpath=\"'resolve-uri(&quot;x&quot;)'\" base-uri=\"http://example.com/base/\"/>")));
            Assert.AreEqual(
                "<out>http://example.com/a</out>",
                Run(Root("<xsl:evaluate xpath=\"'static-base-uri()'\" base-uri=\"http://example.com/{'a'}\"/>")));
        }

        [TestMethod]
        public void AnExpressionMayReturnAClosureOverAParameter()
        {
            Assert.AreEqual(
                "<out>60</out>",
                Run(Root("<xsl:variable name=\"g\" as=\"function(*)\"><xsl:evaluate xpath=\"'function($x) { $x + $y }'\">"
                    + "<xsl:with-param name=\"y\" select=\"42\"/></xsl:evaluate></xsl:variable><xsl:value-of select=\"$g(18)\"/>")));
        }

        [TestMethod]
        public void ANameTheStylesheetNeverWroteIsFoundInTheDocument()
        {
            // The name has no slot until the target is compiled, and the mapping the transformation started
            // with does not reach it; the second transformation over the same stylesheet started later.
            const string Count = "<xsl:evaluate xpath=\"'count(//zebra)'\" context-item=\"/\"/>";
            Xslt xslt = new Xslt(
                Head + Root(Count) + "</xsl:stylesheet>",
                new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 });

            Assert.AreEqual("<out>2</out>", xslt.TransformXml("<r><zebra/><zebra/></r>"));
            Assert.AreEqual("<out>3</out>", xslt.TransformXml("<r><zebra/><zebra/><zebra/></r>"));
        }

        [TestMethod]
        public void ACompiledTargetIsKeptForWhatItWasCompiledAgainst()
        {
            // The parameter names change from call to call, so what was compiled for one call does not
            // serve the next — and the last one has no $p2 at all.
            const string Loop =
                "<xsl:for-each select=\"2 to 10\"><xsl:variable name=\"r\" as=\"xs:integer\">"
                + "<xsl:evaluate xpath=\"$e\" with-params=\"map{xs:QName('p' || .): position() mod 5}\">"
                + "<xsl:with-param name=\"p1\" select=\"20\"/></xsl:evaluate></xsl:variable>"
                + "<v><xsl:value-of select=\"$r\"/></v></xsl:for-each>";

            Assert.AreEqual(
                "<out><v>21</v><v>21</v><v>21</v><v>21</v><v>21</v><v>21</v><v>21</v><v>21</v><v>21</v></out>",
                Run("<xsl:variable name=\"e\" select=\"'$p1 + 1'\"/>" + Root(Loop)));
            Assert.AreEqual(
                "XPST0008",
                Refuses("<xsl:variable name=\"e\" select=\"'$p1 + $p2'\"/>" + Root(Loop)));
        }

        [TestMethod]
        public void SchemaAwareIsReadForItsSpellingOnly()
        {
            Assert.AreEqual("<out>1</out>", Run(Root("<xsl:evaluate xpath=\"'1'\" schema-aware=\"yes\"/>")));
            Assert.AreEqual("<out>1</out>", Run(Root("<xsl:evaluate xpath=\"'1'\" schema-aware=\"{' false '}\"/>")));
            Assert.AreEqual("XTSE0020", Refuses(Root("<xsl:evaluate xpath=\"'1'\" schema-aware=\"TRUE\"/>")));
            Assert.AreEqual("XTDE0030", Refuses(Root("<xsl:evaluate xpath=\"'1'\" schema-aware=\"{'maybe'}\"/>")));
        }

        [TestMethod]
        public void DynamicEvaluationCanBeSwitchedOff()
        {
            XsltOptions off = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30, DynamicEvaluation = false };

            // Statically disabled, in the specification's terms: the functions say so, a fallback is taken,
            // and reaching an instruction without one is the error the specification names.
            Assert.AreEqual(
                "<out>no false</out>",
                Run(Root("<xsl:value-of select=\"system-property('xsl:supports-dynamic-evaluation'), element-available('xsl:evaluate')\"/>"), options: off));
            Assert.AreEqual(
                "<out>yes true</out>",
                Run(Root("<xsl:value-of select=\"system-property('xsl:supports-dynamic-evaluation'), element-available('xsl:evaluate')\"/>")));
            Assert.AreEqual(
                "<out>instead</out>",
                Run(Root("<xsl:evaluate xpath=\"'1'\"><xsl:fallback>instead</xsl:fallback></xsl:evaluate>"), options: off));
            Assert.AreEqual("XTDE3175", Refuses(Root("<xsl:evaluate xpath=\"'1'\"/>"), options: off));
        }

        [TestMethod]
        public void ATargetExpressionIsCompiledAgainstTheCollationInScope()
        {
            // The specification says it in one line: the default collation of the target expression is the
            // one defined at that point in the stylesheet. Everything else about the expression's static
            // context is taken from the instruction -- the namespaces, the base URI, the default element
            // namespace -- and the collation had been the exception, always the code point one.
            const string Secondary =
                " default-collation=\"http://www.w3.org/2013/collation/UCA?strength=secondary\"";

            // Written down, the collation in scope decides the comparison: at secondary strength case is
            // not a difference.
            Assert.AreEqual(
                "<out>true</out>",
                Run("<xsl:template match=\"/\"" + Secondary
                    + "><out><xsl:value-of select=\"'XYZ' eq 'xyz'\"/></out></xsl:template>"));

            // Evaluated, it has to decide the same one.
            Assert.AreEqual(
                "<out>true</out>",
                Run("<xsl:template match=\"/\"" + Secondary
                    + "><out><xsl:evaluate xpath=\"'&quot;XYZ&quot; eq &quot;xyz&quot;'\"/>"
                    + "</out></xsl:template>"));

            // And with nothing in scope it is the code point collation, where case is a difference.
            Assert.AreEqual(
                "<out>false</out>",
                Run(Root("<xsl:evaluate xpath=\"'&quot;XYZ&quot; eq &quot;xyz&quot;'\"/>")));

            // A default-collation on the instruction itself is what is in scope there, and reaches the
            // target the same way.
            Assert.AreEqual(
                "<out>true</out>",
                Run(Root("<xsl:evaluate" + Secondary
                    + " xpath=\"'&quot;XYZ&quot; eq &quot;xyz&quot;'\"/>")));
        }
    }
}
