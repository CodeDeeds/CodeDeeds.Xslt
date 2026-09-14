namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for namespace nodes: the namespace axis, the <c>namespace-node()</c> test, what such a node
    /// answers for, and what copying one does.
    /// </summary>
    [TestClass]
    public sealed class NamespaceNodeTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">";

        private const string Input = "<r xmlns:a=\"urn:a\"><e xmlns:b=\"urn:b\" xmlns=\"urn:d\"/></r>";

        private static string Run(string body, string input = Input)
        {
            XsltOptions options = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 };
            return new Xslt(Head + body + "</xsl:stylesheet>", options).TransformXml(input);
        }

        private static string Root(string content)
        {
            return "<xsl:template match=\"/\"><out>" + content + "</out></xsl:template>";
        }

        private static string Value(string expression, string input = Input)
        {
            string result = Run(Root("<xsl:value-of select=\"" + expression + "\"/>"), input);
            return result == "<out/>" ? string.Empty : result["<out>".Length..^"</out>".Length];
        }

        [TestMethod]
        public void TheNamespaceAxisHoldsEveryNamespaceInScope()
        {
            // The xml namespace is always among them (XDM §6.4), then what the element and its ancestors
            // declare; an element in scope of a redeclared default namespace has a nameless node for it.
            Assert.AreEqual("a xml", Value("sort(/r/namespace::*/name())"));
            Assert.AreEqual(" a b xml", Value("sort(/r/*/namespace::*/name())"));
            Assert.AreEqual("urn:b", Value("/r/*/namespace::b"));
            Assert.AreEqual("urn:d", Value("/r/*/namespace::*[name() = '']"));
            Assert.AreEqual("", Value("/r/@*/namespace::*"));
        }

        [TestMethod]
        public void ANamespaceNodeAnswersForItsNameAndValue()
        {
            // local-name() and name() are the prefix, namespace-uri() is nothing, the string value is the
            // URI, and the parent is the element. The default namespace's node has no name at all.
            Assert.AreEqual("b|b||urn:b|e", Value("/r/*/namespace::b/(local-name(), name(), namespace-uri(), string(), ../name()) => string-join('|')"));
            Assert.AreEqual("true", Value("empty(node-name(/r/*/namespace::*[name() = '']))"));
            Assert.AreEqual("true", Value("/r/*/namespace::b instance of namespace-node()"));
            Assert.AreEqual("false", Value("/r/*/namespace::b instance of attribute()"));
            Assert.AreEqual("false", Value("/r/*/@* instance of namespace-node()"));
        }

        [TestMethod]
        public void ANamespaceNodeIsTheSameNodeEachTime()
        {
            // Made on first use and kept: the same node comes back however the element is reached, so it
            // is itself and its id is one id; a sibling element has its own node for the same binding.
            Assert.AreEqual("true", Value("/r/*/namespace::b is /r/*/namespace::b"));
            Assert.AreEqual("true", Value("generate-id(/r/*/namespace::a) = generate-id((/r/*/namespace::*)[name() = 'a'])"));
            Assert.AreEqual("false", Value("/r/namespace::a is /r/*/namespace::a"));
            Assert.AreEqual("e xml a b a1", Run(Root(
                "<xsl:for-each select=\"/r/*/(., namespace::b, @*, namespace::a, namespace::xml)\">"
                + "<xsl:sort select=\"1\"/><xsl:value-of select=\"concat(name(), ' ')\"/></xsl:for-each>"),
                "<r><e xmlns:a=\"urn:a\" xmlns:b=\"urn:b\" a1=\"1\"/></r>")
                .Replace("<out>", string.Empty).Replace(" </out>", string.Empty));
        }

        [TestMethod]
        public void ACopiedNamespaceNodeIsADeclaration()
        {
            // Copied into an element it is a declaration on it; at the top of a captured sequence it is a
            // parentless namespace node, which a further copy writes back out as a declaration again.
            Assert.AreEqual(
                "<out><x xmlns:b=\"urn:b\"/></out>",
                Run(Root("<x><xsl:copy-of select=\"/r/*/namespace::b\"/></x>")));
            Assert.AreEqual(
                "<out>1 true<x xmlns:a=\"urn:a\"/></out>",
                Run(Root(
                    "<xsl:variable name=\"n\" as=\"node()*\"><xsl:namespace name=\"a\">urn:a</xsl:namespace></xsl:variable>"
                    + "<xsl:value-of select=\"count($n), $n instance of namespace-node()\"/>"
                    + "<x><xsl:copy-of select=\"$n\"/></x>")));
        }

        [TestMethod]
        public void ATemplateMayMatchANamespaceNode()
        {
            // namespace-node() with no axis written stands on the namespace axis, in a pattern as in a path.
            Assert.AreEqual(
                "<out>[a=urn:a][xml=http://www.w3.org/XML/1998/namespace]</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/namespace::*\"><xsl:sort select=\"name()\"/></xsl:apply-templates></out></xsl:template>"
                    + "<xsl:template match=\"namespace-node()\">[<xsl:value-of select=\"name(), '=', .\" separator=\"\"/>]</xsl:template>"));
            Assert.AreEqual(
                "<out>a</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/namespace::a\"/></out></xsl:template>"
                    + "<xsl:template match=\"namespace::a\"><xsl:value-of select=\"name()\"/></xsl:template>"
                    + "<xsl:template match=\"node()\">wrong</xsl:template>"));
        }

        [TestMethod]
        public void TheNamespacesATreesNamesImplyAreNodesToo()
        {
            // A tree built without recording its declarations — json-to-xml()'s — still has the namespaces
            // its names are in (XDM §6.2.1): each of the two elements has the functions namespace and xml.
            Assert.AreEqual("4", Value("count(json-to-xml('{&quot;a&quot;:1}')//namespace::*)"));
            Assert.AreEqual(
                "p xml",
                Value("sort(parse-xml('&lt;r p:a=&quot;1&quot; xmlns:p=&quot;urn:p&quot;/&gt;')/*/namespace::*/name())"));
        }

        [TestMethod]
        public void AnExcludedPrefixDesignatesTheNamespaceItIsBoundTo()
        {
            // §11.1.4: exclude-result-prefixes names namespaces by the prefixes bound where it is written, so
            // an ancestor excluding c, there bound to c.uri, excludes a descendant's a rebound to c.uri, and
            // the descendant's own c, bound to e.uri, is kept.
            Assert.AreEqual(
                "<out>b=b.uri|b=b.uri c=e.uri</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:variable name=\"t\" as=\"element()\">"
                    + "<alpha xmlns:a=\"a.uri\" xmlns:b=\"b.uri\" xmlns:c=\"c.uri\" xsl:exclude-result-prefixes=\"a c\">"
                    + "<beta xmlns:a=\"c.uri\" xmlns:b=\"d.uri\" xmlns:c=\"e.uri\" xsl:exclude-result-prefixes=\"b\"/></alpha>"
                    + "</xsl:variable><xsl:value-of select=\"string-join(sort($t/namespace::*[name() != 'xml']/(name() || '=' || .)), ' ')\"/>|"
                    + "<xsl:value-of select=\"string-join(sort($t/beta/namespace::*[name() != 'xml']/(name() || '=' || .)), ' ')\"/>"
                    + "</out></xsl:template>"));
        }

        [TestMethod]
        public void ASnapshotOfANamespaceNodeKeepsItsElement()
        {
            Assert.AreEqual("b|e|urn:b|true", Value(
                "snapshot(/r/*/namespace::b)/(name(), ../name(), string(), .. instance of element()) => string-join('|')"));
            Assert.AreEqual("/Q{}r[1]/Q{urn:d}e[1]/namespace::b", Value("path(/r/*/namespace::b)"));
        }

        // ---- What the default namespace is on an element that has not got one --------------------------

        [TestMethod]
        public void AnElementInsideOneThatKeepsItsNamespacesToItselfUndeclaresTheDefault()
        {
            // The constructed element is in a namespace and is written without a prefix, so it declares
            // that namespace as the default. Its child has a prefix and, inherit-namespaces being no,
            // takes none of the parent's namespace nodes — so the default is undeclared on it, which is
            // the one namespace XML 1.0 can undeclare.
            Assert.AreEqual(
                "<a xmlns=\"urn:x\"><p:b xmlns:p=\"urn:y\" xmlns=\"\"/></a>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:element name=\"a\" namespace=\"urn:x\" inherit-namespaces=\"no\">"
                    + "<p:b xmlns:p=\"urn:y\"/></xsl:element></xsl:template>"));

            // With the namespaces inherited, which is the default, the child has the parent's default
            // namespace node and nothing is undeclared.
            Assert.AreEqual(
                "<a xmlns=\"urn:x\"><p:b xmlns:p=\"urn:y\"/></a>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:element name=\"a\" namespace=\"urn:x\">"
                    + "<p:b xmlns:p=\"urn:y\"/></xsl:element></xsl:template>"));
        }

        [TestMethod]
        public void ACopyKeepsAnUndeclarationAndAnElementRebuiltAroundItDoesNot()
        {
            // Copying the whole document attaches it to a document node, which has no namespace nodes to
            // inherit, so the undeclaration the source made survives into the result.
            string source = "<s:c xmlns:s=\"urn:s\" xmlns=\"urn:t\"><s:e xmlns=\"\"/></s:c>";

            Assert.AreEqual(
                "<s:c xmlns:s=\"urn:s\" xmlns=\"urn:t\"><s:e xmlns=\"\"/></s:c>",
                Run("<xsl:template match=\"/\"><xsl:copy-of select=\".\"/></xsl:template>", source));

            // Rebuilding the parent with xsl:copy makes the inner element the child of a newly constructed
            // element, and it takes that element's namespace nodes, the default among them. Which is the
            // rule that loses the undeclaration, and why the two are written out differently.
            Assert.AreEqual(
                "<s:c xmlns:s=\"urn:s\" xmlns=\"urn:t\"><s:e/></s:c>",
                Run(
                    "<xsl:template match=\"/\"><xsl:apply-templates/></xsl:template>"
                    + "<xsl:template match=\"*\"><xsl:copy><xsl:apply-templates/></xsl:copy></xsl:template>",
                    source));
        }

        // ---- A namespace node that takes the prefix the element's own name was written with -------------

        [TestMethod]
        public void AnElementGivesUpItsPrefixToANamespaceNodeThatClaimsIt()
        {
            // The element has no namespace node of its own for p — the stylesheet's binding is excluded —
            // so the one xsl:namespace makes is the only one, and it stands. The element's name cannot be
            // written with a prefix bound elsewhere, so it takes another (XSLT 3.0 §11.7).
            Assert.AreEqual(
                "<p_0:item xmlns:p_0=\"http://p.uri/\" xmlns:p=\"http://q.uri/\"/>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<p:item xmlns:p=\"http://p.uri/\" xsl:exclude-result-prefixes=\"p\">"
                    + "<xsl:namespace name=\"p\">http://q.uri/</xsl:namespace></p:item></xsl:template>"));

            // The same where a tree is being built, and there the name is changed rather than rewritten.
            Assert.AreEqual(
                "<out>false</out>",
                Run(
                    "<xsl:template match=\"/\"><xsl:variable name=\"e\" as=\"element()\">"
                    + "<xsl:element name=\"p:item\" xmlns:p=\"http://p.uri/\">"
                    + "<xsl:namespace name=\"p\">http://q.uri/</xsl:namespace></xsl:element></xsl:variable>"
                    + "<out><xsl:value-of select=\"starts-with(name($e), 'p:')\"/></out></xsl:template>"));
        }

        [TestMethod]
        public void TwoNamespaceNodesOfOneNameAndDifferentValuesAreRefused()
        {
            // Where the element does have a namespace node of its own for the prefix, a second one of the
            // same name is a contradiction rather than something a new prefix can move out of the way of.
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(
                "<xsl:template match=\"/\"><p:item xmlns:p=\"http://p.uri/\">"
                + "<xsl:namespace name=\"p\">http://q.uri/</xsl:namespace></p:item></xsl:template>"));

            Assert.AreEqual("XTDE0430", error.Code);

            XsltException twice = Assert.ThrowsExactly<XsltException>(() => Run(
                "<xsl:template match=\"/\"><name>"
                + "<xsl:namespace name=\"a\" select=\"'http://alpha/'\"/>"
                + "<xsl:namespace name=\"a\" select=\"'http://beta/'\"/></name></xsl:template>"));

            Assert.AreEqual("XTDE0430", twice.Code);
        }

        [TestMethod]
        public void TheXmlPrefixIsNeverDeclaredInTheOutput()
        {
            // Every element has a namespace node for the xml prefix, so copying the axis brings one along.
            // The binding is implicit in every XML document and writing it would be noise the reader has to
            // discount; serialization leaves it out.
            Assert.AreEqual(
                "<out><x xmlns:a=\"urn:a\"/></out>",
                Run(Root("<x><xsl:copy-of select=\"/r/namespace::*\"/></x>")));

            // The same where the node is asked for by name rather than swept up by the axis.
            Assert.AreEqual(
                "<out><x/></out>",
                Run(Root("<x><xsl:copy-of select=\"/r/namespace::xml\"/></x>")));

            // And where an attribute in the xml namespace puts the prefix on the element.
            Assert.AreEqual(
                "<out><x xml:lang=\"en\"/></out>",
                Run(Root("<x xml:lang=\"en\"/>")));
        }

        [TestMethod]
        public void ANamespaceNodeHasNoBaseUri()
        {
            // A namespace node is not a place in the document a relative reference could be written from,
            // so the data model gives it the empty sequence rather than the base URI of its element.
            Assert.AreEqual("true", Value("empty(base-uri(/r/namespace::a))"));
            Assert.AreEqual("true", Value("empty(/r/namespace::a/base-uri())"));

            // The element it is attached to does have one where xml:base says so, which is what makes
            // the answer above a rule about namespace nodes rather than a tree with nowhere to point at.
            const string Based = "<r xml:base=\"http://example.org/\" xmlns:a=\"urn:a\"/>";

            Assert.AreEqual("http://example.org/", Value("base-uri(/r)", Based));
            Assert.AreEqual("true", Value("empty(base-uri(/r/namespace::a))", Based));
        }

        [TestMethod]
        public void AKeyMayIndexNamespaceNodes()
        {
            // From 3.0 a match pattern may name namespace-node(), so a key can file one. The index is built
            // over elements and their attributes; namespace nodes are made on demand and are walked only
            // for a key whose pattern asks for that kind.
            const string Document = "<r xmlns:a=\"urn:a\"><e xmlns:b=\"urn:b\"/><f xmlns:a=\"urn:a\"/></r>";

            Assert.AreEqual(
                "<out>r e f</out>",
                Run(
                    "<xsl:key name=\"ns\" match=\"namespace-node()\" use=\"name()\"/>"
                    + Root("<xsl:value-of select=\"key('ns', 'a')/../name()\"/>"),
                    Document));

            Assert.AreEqual(
                "<out>urn:b</out>",
                Run(
                    "<xsl:key name=\"ns\" match=\"namespace-node()\" use=\"string()\"/>"
                    + Root("<xsl:value-of select=\"key('ns', 'urn:b')\"/>"),
                    Document));

            // A key that cannot match a namespace node files none, which is what keeps the walk off every
            // other stylesheet's index.
            Assert.AreEqual(
                "<out>0</out>",
                Run(
                    "<xsl:key name=\"named\" match=\"*\" use=\"name()\"/>"
                    + Root("<xsl:value-of select=\"count(key('named', 'a'))\"/>"),
                    Document));
        }

        [TestMethod]
        public void CurrentInsideAKeyUseIsTheNodeBeingIndexed()
        {
            // There is no other current node while an index is built. A stylesheet reaches for it to read
            // something of the node from an expression whose own context has moved elsewhere — here onto
            // the namespace axis, to turn a prefix written in an attribute into the URI it stands for.
            const string Document =
                "<r xmlns:a=\"urn:a\" xmlns:b=\"urn:b\">"
                + "<t n=\"1\" ref=\"a:x\"/><t n=\"2\" ref=\"b:x\"/><t n=\"3\" ref=\"a:y\"/></r>";

            Assert.AreEqual(
                "<out>1 3</out>",
                Run(
                    "<xsl:key name=\"t\" match=\"t\""
                    + " use=\"string(namespace::*[name() = substring-before(current()/@ref, ':')])\"/>"
                    + Root("<xsl:value-of select=\"key('t', 'urn:a')/@n\"/>"),
                    Document));
        }

        [TestMethod]
        public void ACopiedNamespaceNodeTakesTheElementsPrefixFromIt()
        {
            // Namespace fixup, on the copy path rather than the xsl:namespace one: the copied node keeps the
            // prefix it came with and the element takes another for the namespace it is in. Dropping the
            // node instead would lose a part of the result the stylesheet asked for.
            string result = Run(
                "<xsl:template match=\"/\">"
                + "<xsl:element name=\"ns:e\" namespace=\"urn:one\">"
                + "<xsl:copy-of select=\"/r/namespace::ns\"/></xsl:element></xsl:template>",
                "<r xmlns:ns=\"urn:two\"/>");

            StringAssert.Contains(result, "xmlns:ns=\"urn:two\"");
            Assert.IsFalse(result.StartsWith("<ns:e", StringComparison.Ordinal), "the element gave up the prefix");

            // The namespace node survives into a tree the same way, where it can be asked for rather than read.
            Assert.AreEqual(
                "<out>urn:two</out>",
                Run(
                    "<xsl:template match=\"/\"><xsl:variable name=\"e\" as=\"element()\">"
                    + "<xsl:element name=\"ns:e\" namespace=\"urn:one\">"
                    + "<xsl:copy-of select=\"/r/namespace::ns\"/></xsl:element></xsl:variable>"
                    + "<out><xsl:value-of select=\"$e/namespace::ns\"/></out></xsl:template>",
                    "<r xmlns:ns=\"urn:two\"/>"));
        }
    }
}
