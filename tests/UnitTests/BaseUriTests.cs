using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the base URI of what a stylesheet builds: a temporary tree takes its declaration's, a
    /// parentless copy keeps the copied node's, and <c>document()</c> resolves from where it is written.
    /// </summary>
    [TestClass]
    public sealed class BaseUriTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private sealed class Documents : IXsltResolver
        {
            public List<(string Href, string? Base)> Asked { get; } = new();

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                Asked.Add((href, baseUri));
                return new ResolvedResource(
                    new StringReader($"<got base=\"{baseUri}\"/>"),
                    new Uri(new Uri(baseUri ?? "http://x/"), href).AbsoluteUri);
            }
        }

        private static string Sheet(string declarations, string body, string xmlBase = "http://www.example.org/")
        {
            return $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\" xml:base=\"{xmlBase}\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + declarations
                + "<xsl:template match=\"/\"><out>" + body + "</out></xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, string input = "<doc xml:base=\"http://in.example.com/xml/\"><e xml:base=\"sub/\"/></doc>", IXsltResolver? documents = null)
        {
            return new Xslt(
                stylesheet,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    DocumentResolver = documents,
                }).TransformXml(input);
        }

        [TestMethod]
        public void ATemporaryTreeTakesTheBaseUriOfItsDeclaration()
        {
            // The document node's base URI is the xsl:variable's (§9.4), which its own xml:base moved; an
            // element inside inherits it, and one with an xml:base of its own resolves that against it.
            Assert.AreEqual(
                "<out>http://www.example.org/main/|http://www.example.org/main/|http://www.example.org/main/deeper/</out>",
                Run(Sheet(
                    string.Empty,
                    "<xsl:variable name=\"t\" xml:base=\"/main/\"><a><b xml:base=\"deeper/\"/></a></xsl:variable>"
                    + "<xsl:value-of select=\"base-uri($t), base-uri($t/a), base-uri($t/a/b)\" separator=\"|\"/>")));

            // With an as declaration the nodes are parentless, and each takes the declaration's base URI.
            Assert.AreEqual(
                "<out>http://www.example.org/main/</out>",
                Run(Sheet(
                    string.Empty,
                    "<xsl:variable name=\"t\" as=\"element()\" xml:base=\"/main/\"><a/></xsl:variable>"
                    + "<xsl:value-of select=\"base-uri($t)\"/>")));

            // A global variable's tree, too, and a tree built with no xml:base anywhere is relative to the
            // stylesheet as before.
            Assert.AreEqual(
                "<out>http://www.example.org/g/|http://www.example.org/</out>",
                Run(Sheet(
                    "<xsl:variable name=\"g\" xml:base=\"g/\"><a/></xsl:variable>",
                    "<xsl:variable name=\"plain\"><a/></xsl:variable>"
                    + "<xsl:value-of select=\"base-uri($g), base-uri($plain)\" separator=\"|\"/>")));
        }

        [TestMethod]
        public void AParentlessCopyKeepsTheBaseUriOfWhatItCopies()
        {
            // xsl:copy copies no attributes, so the copy keeps the base URI the original had (§11.9.1);
            // under a parent it takes the parent's like any node.
            Assert.AreEqual(
                "<out>http://in.example.com/xml/sub/|http://www.example.org/main/</out>",
                Run(Sheet(
                    string.Empty,
                    "<xsl:variable name=\"alone\" as=\"element()\" xml:base=\"/main/\"><xsl:for-each select=\"doc/e\"><xsl:copy/></xsl:for-each></xsl:variable>"
                    + "<xsl:variable name=\"held\" xml:base=\"/main/\"><xsl:for-each select=\"doc/e\"><xsl:copy/></xsl:for-each></xsl:variable>"
                    + "<xsl:value-of select=\"base-uri($alone), base-uri($held/e)\" separator=\"|\"/>")));

            // xsl:copy-of copies the xml:base attribute with the element, and a parentless copy resolves it
            // against the base URI of the instruction copying it (§11.9.2).
            Assert.AreEqual(
                "<out>http://www.example.org/sub/|http://in.example.com/xml/</out>",
                Run(Sheet(
                    string.Empty,
                    "<xsl:variable name=\"withBase\" as=\"element()\"><xsl:copy-of select=\"doc/e\"/></xsl:variable>"
                    + "<xsl:variable name=\"whole\" as=\"element()\"><xsl:copy-of select=\"doc\"/></xsl:variable>"
                    + "<xsl:value-of select=\"base-uri($withBase), base-uri($whole/e/..)\" separator=\"|\"/>")));

            // A document node copied shallowly into a sequence — by xsl:copy, or by a mode's built-in
            // shallow-copy rule — is a document node with the base URI of the one it copies, not the
            // variable's. An input handed over as text is relative to the stylesheet, as it always was.
            Assert.AreEqual(
                "<out>http://www.example.org/|http://www.example.org/xml/|http://www.example.org/</out>",
                Run(Sheet(
                    "<xsl:mode name=\"shallow\" on-no-match=\"shallow-copy\"/><xsl:template match=\"/*\" mode=\"shallow\"/>",
                    "<xsl:variable name=\"copied\" as=\"document-node()\" xml:base=\"/other/\"><xsl:copy><xsl:copy-of select=\"*\"/></xsl:copy></xsl:variable>"
                    + "<xsl:variable name=\"applied\" as=\"document-node()\" xml:base=\"/other/\"><xsl:apply-templates select=\".\" mode=\"shallow\"/></xsl:variable>"
                    + "<xsl:value-of select=\"base-uri($copied), base-uri($copied/doc), base-uri($applied)\" separator=\"|\"/>"),
                    input: "<doc xml:base=\"xml/\"/>"));
        }

        [TestMethod]
        public void DocumentResolvesFromWhereItIsWritten()
        {
            // The same reference from two elements with different base URIs names two documents: the cache
            // is keyed on what the reference resolves against as well as on the reference.
            Documents documents = new Documents();

            Assert.AreEqual(
                "<out><got base=\"http://www.example.org/a/\"/><got base=\"http://www.example.org/b/\"/><got base=\"http://www.example.org/a/\"/></out>",
                Run(
                    Sheet(
                        string.Empty,
                        "<xsl:for-each select=\"1\" xml:base=\"a/\"><xsl:copy-of select=\"document('x.xml')\"/></xsl:for-each>"
                        + "<xsl:for-each select=\"1\" xml:base=\"b/\"><xsl:copy-of select=\"document('x.xml')\"/></xsl:for-each>"
                        + "<xsl:for-each select=\"1\" xml:base=\"a/\"><xsl:copy-of select=\"document('x.xml')\"/></xsl:for-each>"),
                    documents: documents));

            // The third reading was the first document again, so the resolver was asked twice.
            Assert.HasCount(2, documents.Asked);
        }
    }
}
