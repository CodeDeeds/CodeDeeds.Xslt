using System.Xml;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the fallback mechanism: <c>xsl:fallback</c>, and the <c>element-available()</c> and
    /// <c>function-available()</c> functions a stylesheet uses to find out what it is running on.
    /// </summary>
    /// <remarks>
    /// What all of this turns on is <em>when</em> something is rejected. A stylesheet may quite legitimately
    /// contain an instruction this engine has never heard of, guarded so that it is never reached — so the
    /// error has to wait until the instruction is actually instantiated, rather than being raised while the
    /// stylesheet is compiled.
    /// </remarks>
    [TestClass]
    public sealed class FallbackTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";
        private const string Input = "<r t=\"T\"><i>a</i><i>b</i></r>";

        private static string Run(string stylesheet, string input = Input, XsltVersion? version = null)
        {
            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                Version = version ?? XsltVersion.Implemented,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        private static void AssertMatchesReference(
            string stylesheet, string input = Input, XsltVersion? version = null)
        {
            XslCompiledTransform transform = new XslCompiledTransform();
            using (XmlReader reader = XmlReader.Create(new StringReader(stylesheet)))
            {
                transform.Load(reader);
            }

            StringWriter output = new StringWriter();
            XmlWriterSettings settings = transform.OutputSettings!.Clone();
            settings.OmitXmlDeclaration = true;
            settings.ConformanceLevel = ConformanceLevel.Auto;

            using (XmlWriter writer = XmlWriter.Create(output, settings))
            using (XmlReader reader = XmlReader.Create(new StringReader(input)))
            {
                transform.Transform(reader, null, writer);
            }

            Assert.AreEqual(
                XmlComparison.Normalize(output.ToString()),
                XmlComparison.Normalize(Run(stylesheet, input, version)),
                "output differed from XslCompiledTransform");
        }

        /// <summary>Wraps a stylesheet body, declaring an extension namespace but not designating it as one.</summary>
        private static string Sheet(string body, string version = "1.0", string extensionPrefixes = "")
        {
            string designation = extensionPrefixes.Length == 0
                ? string.Empty
                : $" extension-element-prefixes=\"{extensionPrefixes}\"";

            return $"<xsl:stylesheet version=\"{version}\" {Xsl} xmlns:e=\"urn:ext\"{designation}>"
                + body
                + "</xsl:stylesheet>";
        }

        /// <summary>
        /// What a probe writes. The stylesheet these tests build declares <c>xmlns:e</c>, which the literal
        /// <c>out</c> element inherits and carries into the result.
        /// </summary>
        private static string Answer(bool value)
        {
            return $"<out xmlns:e=\"urn:ext\">{(value ? "true" : "false")}</out>";
        }

        /// <summary>Wraps an expression in a stylesheet that writes its value.</summary>
        private static string Probe(string expression)
        {
            return Sheet(
                "<xsl:template match=\"/\"><out><xsl:value-of select=\"" + expression + "\"/></out></xsl:template>");
        }

        // ---- element-available() -------------------------------------------------------------------------

        /// <summary>
        /// Only the elements that may appear in a template body count as instructions. A declaration such as
        /// <c>xsl:template</c> is not one, and neither is a part of an instruction such as <c>xsl:sort</c> or
        /// <c>xsl:when</c>, which cannot stand on their own.
        /// </summary>
        [TestMethod]
        [DataRow("xsl:apply-imports")]
        [DataRow("xsl:apply-templates")]
        [DataRow("xsl:attribute")]
        [DataRow("xsl:call-template")]
        [DataRow("xsl:choose")]
        [DataRow("xsl:comment")]
        [DataRow("xsl:copy")]
        [DataRow("xsl:copy-of")]
        [DataRow("xsl:element")]
        [DataRow("xsl:fallback")]
        [DataRow("xsl:for-each")]
        [DataRow("xsl:if")]
        [DataRow("xsl:message")]
        [DataRow("xsl:number")]
        [DataRow("xsl:processing-instruction")]
        [DataRow("xsl:text")]
        [DataRow("xsl:value-of")]
        [DataRow("xsl:variable")]
        [DataRow("xsl:template")]
        [DataRow("xsl:sort")]
        [DataRow("xsl:param")]
        [DataRow("xsl:with-param")]
        [DataRow("xsl:when")]
        [DataRow("xsl:otherwise")]
        [DataRow("xsl:stylesheet")]
        [DataRow("xsl:output")]
        [DataRow("xsl:key")]
        [DataRow("xsl:import")]
        [DataRow("xsl:include")]
        [DataRow("xsl:attribute-set")]
        [DataRow("xsl:decimal-format")]
        [DataRow("xsl:namespace-alias")]
        [DataRow("xsl:preserve-space")]
        [DataRow("xsl:no-such-instruction")]
        [DataRow("e:thing")]
        [DataRow("unprefixed")]
        public void ElementAvailableAgreesWithTheReference(string name)
        {
            // Compared on a processor asked to be 2.0. The reference is a 1.0 processor, and from 3.0 the
            // function answers for every element the specification defines, declarations included, rather
            // than for the instructions alone.
            AssertMatchesReference(Probe($"element-available('{name}')"), version: XsltVersion.V20);
        }

        /// <summary>
        /// The instructions XSLT 2.0 adds, which this engine implements and the reference processor does not.
        /// </summary>
        /// <remarks>
        /// No oracle here, deliberately: <c>XslCompiledTransform</c> is a 1.0 processor and answers false for
        /// every one of these. Agreeing with it would mean reporting instructions as unavailable that a
        /// stylesheet can use, which is the answer that makes a guarded call fall back for no reason.
        /// </remarks>
        [TestMethod]
        [DataRow("xsl:analyze-string")]
        [DataRow("xsl:document")]
        [DataRow("xsl:for-each-group")]
        [DataRow("xsl:namespace")]
        [DataRow("xsl:next-match")]
        [DataRow("xsl:perform-sort")]
        [DataRow("xsl:result-document")]
        [DataRow("xsl:sequence")]
        public void ElementAvailableReportsTheImplementedTwoPointZeroInstructions(string name)
        {
            Assert.AreEqual(Answer(true), Run(Probe($"element-available('{name}')")));
        }

        /// <summary>
        /// The XSLT 2.0 elements that are not instructions this engine can instantiate: declarations, and
        /// parts of an instruction rather than instructions in their own right.
        /// </summary>
        [TestMethod]
        [DataRow("xsl:function")]
        [DataRow("xsl:character-map")]
        [DataRow("xsl:output-character")]
        [DataRow("xsl:import-schema")]
        [DataRow("xsl:matching-substring")]
        public void ElementAvailableIsFalseForWhatIsNotAnAvailableInstruction(string name)
        {
            // On a 2.0 processor; from 3.0 the function answers for every element the specification defines.
            Assert.AreEqual(
                Answer(false), Run(Probe($"element-available('{name}')"), version: XsltVersion.V20));
        }

        [TestMethod]
        public void ElementAvailableAnswersForTwoPointZeroUnderEitherVersion()
        {
            // The question is what the engine can instantiate, not what the declared version defines, so the
            // answer does not move with the version attribute.
            string probe = "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"element-available('xsl:for-each-group')\"/></out></xsl:template>";

            Assert.AreEqual(Answer(true), Run(Sheet(probe, version: "1.0")));
            Assert.AreEqual(Answer(true), Run(Sheet(probe, version: "2.0")));
        }

        [TestMethod]
        public void ElementAvailableIsFalseForADeclaredExtensionNamespace()
        {
            // Declaring a namespace as an extension namespace does not make its elements available; it only
            // says that elements in it are instructions rather than result content.
            AssertMatchesReference(
                Sheet(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"element-available('e:thing')\"/></out></xsl:template>",
                    extensionPrefixes: "e"));
        }

        [TestMethod]
        public void ANonLiteralElementNameIsResolvedAtRunTime()
        {
            AssertMatchesReference(Probe("element-available(concat('xsl',':','if'))"));
        }

        [TestMethod]
        public void AnUnboundPrefixInAnElementNameIsRejected()
        {
            // The reference rejects this too. A prefix that is not bound is a mistake in the stylesheet, not a
            // question about what the processor supports.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Probe("element-available('nope:thing')")));

            StringAssert.Contains(error.Message, "nope");
        }

        // ---- function-available() ------------------------------------------------------------------------

        [TestMethod]
        [DataRow("concat")]
        [DataRow("lang")]
        [DataRow("count")]
        [DataRow("normalize-space")]
        [DataRow("substring")]
        [DataRow("key")]
        [DataRow("document")]
        [DataRow("format-number")]
        [DataRow("current")]
        [DataRow("generate-id")]
        [DataRow("system-property")]
        [DataRow("element-available")]
        [DataRow("function-available")]
        [DataRow("no-such-function")]
        [DataRow("node-set")]
        [DataRow("e:thing")]
        [DataRow("xsl:key")]
        public void FunctionAvailableAgreesWithTheReference(string name)
        {
            AssertMatchesReference(Probe($"function-available('{name}')"));
        }

        [TestMethod]
        public void IdIsReportedAvailable()
        {
            // id() answers for xml:id attributes, which are IDs by their own specification whether or not a
            // DTD is read, and for what a document type declaration types — so the function is there, and
            // says so.
            Assert.AreEqual(
                "<out xmlns:e=\"urn:ext\">true</out>", Run(Probe("function-available('id')")));
        }

        [TestMethod]
        public void UnparsedEntityUriIsAvailableAndAnswersNothing()
        {
            // There are no unparsed entities without a document type declaration, and the specification's
            // answer for an entity that is not there is a zero-length string — so the function is there,
            // and answers honestly.
            Assert.AreEqual(
                "<out xmlns:e=\"urn:ext\">true</out>", Run(Probe("function-available('unparsed-entity-uri')")));
            Assert.AreEqual(
                "<out xmlns:e=\"urn:ext\">0</out>", Run(Probe("string-length(unparsed-entity-uri('x'))")));
        }

        [TestMethod]
        public void ANonLiteralFunctionNameIsResolvedAtRunTime()
        {
            AssertMatchesReference(Probe("function-available(concat('gener','ate-id'))"));
        }

        // ---- Unimplemented instructions ------------------------------------------------------------------

        [TestMethod]
        public void AnUnknownInstructionIsRejectedWhenTheStylesheetClaimsVersionOne()
        {
            // Nothing forwards-compatible about it: the stylesheet says it is XSLT 1.0, so an element in the
            // XSLT namespace that XSLT 1.0 does not define is a mistake, and saying so at once is a kindness.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet("<xsl:template match=\"/\"><out><xsl:invented/></out></xsl:template>")));

            StringAssert.Contains(error.Message, "xsl:invented");
        }

        // A stylesheet claiming 4.0 is claiming a version later than this engine implements, which is what
        // forwards-compatible processing is for. It was 3.0 in these tests while the engine claimed 2.0.

        [TestMethod]
        public void AnUnknownInstructionFallsBackWhenTheStylesheetClaimsALaterVersion()
        {
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:invented><xsl:fallback>fell back</xsl:fallback></xsl:invented>"
                + "</out></xsl:template>",
                version: "4.0"));
        }

        [TestMethod]
        public void EveryFallbackChildIsInstantiated()
        {
            // The specification instantiates all of them, and content that is not xsl:fallback is skipped.
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out><xsl:invented>"
                + "<xsl:fallback>one</xsl:fallback><ignored/><xsl:fallback>two</xsl:fallback>"
                + "</xsl:invented></out></xsl:template>",
                version: "4.0"));
        }

        [TestMethod]
        public void AFallbackBodyRunsInTheContextTheInstructionWouldHaveHad()
        {
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/r\"><out><xsl:for-each select=\"i\"><xsl:invented>"
                + "<xsl:fallback><xsl:value-of select=\"concat(../@t,.)\"/></xsl:fallback>"
                + "</xsl:invented></xsl:for-each></out></xsl:template>",
                version: "4.0"));
        }

        [TestMethod]
        public void ContentOtherThanFallbackIsNotCompiledAtAll()
        {
            // The instruction's real content was written for a processor that understands it, so what it means
            // here is unknowable — and compiling it would let a nested unknown instruction, which that
            // processor would never have reached, break a stylesheet that runs perfectly well.
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out><xsl:invented>"
                + "<xsl:also-invented/>"
                + "<xsl:fallback>fell back</xsl:fallback>"
                + "</xsl:invented></out></xsl:template>",
                version: "4.0"));
        }

        [TestMethod]
        public void AnUnknownInstructionWithNoFallbackFailsOnlyWhenItIsReached()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet(
                    "<xsl:template match=\"/\"><out><xsl:invented/></out></xsl:template>",
                    version: "4.0")));

            StringAssert.Contains(error.Message, "xsl:invented");
        }

        [TestMethod]
        public void AnUnknownInstructionThatIsNeverReachedIsNotAnError()
        {
            // The point of deferring the error. This stylesheet is perfectly usable here even though part of
            // it is meant for a processor that this is not.
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out>ok</out></xsl:template>"
                + "<xsl:template match=\"never\"><xsl:invented/></xsl:template>",
                version: "4.0"));
        }

        [TestMethod]
        public void AVersionBetweenOneAndTwoIsBehindThisProcessorRatherThanAhead()
        {
            // Forwards-compatible processing is for a version later than the processor implements. When this
            // engine reported 1.0, that included 1.1; now that it reports 3.0 it does not, so the unknown
            // instruction is a mistake this engine can see at compile time and says so — while the version
            // still selects the 1.0 reading of everything 2.0 redefined.
            //
            // The reference processor falls back here instead, for the same reason it answers
            // system-property('xsl:version') differently: it implements 1.0 and this does not.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:invented><xsl:fallback>fell back</xsl:fallback></xsl:invented>"
                    + "</out></xsl:template>",
                    version: "1.1")));

            StringAssert.Contains(error.Message, "xsl:invented");
            Assert.IsTrue(XsltVersion.Parse("1.1").IsBackwardsCompatible, "and 1.1 still reads as 1.0 did");
        }

        [TestMethod]
        public void VersionOnALiteralResultElementTurnsOnForwardsCompatibleProcessing()
        {
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out xsl:version=\"4.0\">"
                + "<xsl:invented><xsl:fallback>fell back</xsl:fallback></xsl:invented>"
                + "</out></xsl:template>"));
        }

        [TestMethod]
        public void APlainVersionAttributeOnAResultElementIsNotADirective()
        {
            // version="2.0" written without the xsl prefix is part of the result, not a message to the
            // processor, so this stylesheet is still XSLT 1.0 and the unknown instruction is still an error.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet(
                    "<xsl:template match=\"/\"><out version=\"2.0\">"
                    + "<xsl:invented><xsl:fallback>fell back</xsl:fallback></xsl:invented>"
                    + "</out></xsl:template>")));

            StringAssert.Contains(error.Message, "xsl:invented");
        }

        [TestMethod]
        public void ASimplifiedStylesheetCanUseForwardsCompatibleProcessing()
        {
            AssertMatchesReference(
                $"<out {Xsl} xsl:version=\"4.0\">"
                + "<xsl:invented><xsl:fallback>fell back</xsl:fallback></xsl:invented></out>");
        }

        // ---- xsl:fallback where nothing failed -----------------------------------------------------------

        [TestMethod]
        public void FallbackUnderAnImplementedInstructionProducesNothing()
        {
            // Its parent worked, so there is nothing to fall back from.
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/r\"><out>"
                + "<xsl:for-each select=\"i\"><xsl:value-of select=\".\"/>"
                + "<xsl:fallback>never</xsl:fallback></xsl:for-each>"
                + "</out></xsl:template>"));
        }

        [TestMethod]
        public void FallbackUnderALiteralResultElementProducesNothing()
        {
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out><xsl:fallback>never</xsl:fallback>kept</out></xsl:template>"));
        }

        // ---- Extension elements --------------------------------------------------------------------------

        [TestMethod]
        public void AnElementInAnUndeclaredNamespaceIsResultContent()
        {
            // Nothing about the element itself says it is an extension; without the designation it is markup
            // to copy out, which is what makes the same stylesheet work on a processor that was never told.
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out><e:thing/></out></xsl:template>"));
        }

        [TestMethod]
        public void ADesignatedExtensionElementFallsBack()
        {
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out><wrap>"
                + "<e:thing><xsl:fallback>fell back</xsl:fallback></e:thing>"
                + "</wrap></out></xsl:template>",
                extensionPrefixes: "e"));
        }

        [TestMethod]
        public void AnExtensionElementWithNoFallbackFailsOnlyWhenItIsReached()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet(
                    "<xsl:template match=\"/\"><out><e:thing/></out></xsl:template>",
                    extensionPrefixes: "e")));

            StringAssert.Contains(error.Message, "e:thing");
        }

        [TestMethod]
        public void AnExtensionElementThatIsNeverReachedIsNotAnError()
        {
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out>ok</out></xsl:template>"
                + "<xsl:template match=\"never\"><e:thing/></xsl:template>",
                extensionPrefixes: "e"));
        }

        [TestMethod]
        public void AnExtensionPrefixCanBeDesignatedOnALiteralResultElement()
        {
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out xmlns:x=\"urn:other\" xsl:extension-element-prefixes=\"x\">"
                + "<x:thing><xsl:fallback>fell back</xsl:fallback></x:thing>"
                + "</out></xsl:template>"));
        }

        [TestMethod]
        public void AnExtensionNamespaceIsNotCopiedToTheResult()
        {
            // It was declared so that the stylesheet could name extension elements. Carrying it into the
            // result would leak a detail of how the stylesheet was written.
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out/></xsl:template>",
                extensionPrefixes: "e"));
        }

        // ---- Extension functions -------------------------------------------------------------------------

        [TestMethod]
        public void AGuardedExtensionFunctionCallLetsTheStylesheetRun()
        {
            // The whole reason function-available() exists, and the reason an unavailable extension function
            // cannot be rejected where it is written: the guard is what keeps the call from ever being made.
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/r\"><out><xsl:choose>"
                + "<xsl:when test=\"function-available('e:node-set')\">"
                + "<xsl:value-of select=\"e:node-set(.)\"/></xsl:when>"
                + "<xsl:otherwise><xsl:value-of select=\"@t\"/></xsl:otherwise>"
                + "</xsl:choose></out></xsl:template>"));
        }

        [TestMethod]
        public void AnExtensionFunctionInAnUnreachedTemplateIsNotAnError()
        {
            AssertMatchesReference(Sheet(
                "<xsl:template match=\"/\"><out>ok</out></xsl:template>"
                + "<xsl:template match=\"never\"><xsl:value-of select=\"e:node-set(.)\"/></xsl:template>"));
        }

        [TestMethod]
        public void CallingAnUnavailableExtensionFunctionFailsWhenItIsEvaluated()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet(
                    "<xsl:template match=\"/\"><out><xsl:value-of select=\"e:node-set(.)\"/></out></xsl:template>")));

            StringAssert.Contains(error.Message, "e:node-set");
            StringAssert.Contains(error.Message, "urn:ext");
        }

        [TestMethod]
        public void AnUnboundPrefixInAFunctionCallIsRejected()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet(
                    "<xsl:template match=\"/\"><out><xsl:value-of select=\"nope:thing()\"/></out></xsl:template>")));

            StringAssert.Contains(error.Message, "nope");
        }

        [TestMethod]
        public void AnUnknownUnprefixedFunctionIsStillRejectedAtCompileTime()
        {
            // A name in no namespace can only be the core library, so this is a typo rather than a call to
            // something another processor might provide. Catching it early is worth more than deferring it.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet(
                    "<xsl:template match=\"/\"><out><xsl:value-of select=\"conat('a','b')\"/></out></xsl:template>")));

            StringAssert.Contains(error.Message, "conat");
        }
        // ---- What an extension instruction falls back to ---------------------------------------------------

        [TestMethod]
        public void AnEmptyFallbackIsStillAFallback()
        {
            // An xsl:fallback with nothing in it says to do nothing, which is a thing to do. Only the
            // absence of one makes reaching an unimplemented instruction an error, so what the fallback
            // compiled to cannot be what decides.
            Assert.AreEqual(
                "<out/>",
                Run(Sheet(
                    "<xsl:template match=\"/\"><out><e:special><xsl:fallback/></e:special></out>"
                    + "</xsl:template>",
                    version: "3.0",
                    extensionPrefixes: "e")));

            Assert.AreEqual(
                "<out>instead</out>",
                Run(Sheet(
                    "<xsl:template match=\"/\"><out><e:special>"
                    + "<xsl:fallback>instead</xsl:fallback></e:special></out></xsl:template>",
                    version: "3.0",
                    extensionPrefixes: "e")));
        }

        [TestMethod]
        public void AnExtensionInstructionWithNoFallbackCarriesItsCode()
        {
            // XTDE1450, and dynamic: the stylesheet is entitled to compile, and reaching the instruction
            // is what fails.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet(
                    "<xsl:template match=\"/\"><out><e:special/></out></xsl:template>",
                    version: "3.0",
                    extensionPrefixes: "e")));

            Assert.AreEqual("XTDE1450", error.Code);
        }

        [TestMethod]
        public void AReservedNamespaceCannotBeAnExtensionNamespace()
        {
            // The specifications have already given those namespaces a meaning, so an element in one is
            // what they say it is rather than an instruction some processor might implement. Static, and
            // an xsl:fallback does not make it otherwise.
            string stylesheet =
                $"<xsl:stylesheet version=\"3.0\" {Xsl} xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " extension-element-prefixes=\"xs\">"
                + "<xsl:template match=\"/\"><out><xs:special><xsl:fallback/></xs:special></out>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "XTSE0085",
                Assert.ThrowsExactly<XsltException>(() => Run(stylesheet)).Code);
        }

        [TestMethod]
        public void NamingOneAsAnExtensionNamespaceIsTheSameError()
        {
            // §24 states the rule twice under the one code: a reserved namespace in the name of an
            // extension instruction, and a prefix bound to one in extension-element-prefixes. So the
            // attribute is refused whether or not anything of that namespace is then written.
            string stylesheet =
                $"<xsl:stylesheet version=\"3.0\" {Xsl} xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " extension-element-prefixes=\"xs\">"
                + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "XTSE0085",
                Assert.ThrowsExactly<XsltException>(() => Run(stylesheet)).Code);
        }
    }
}
