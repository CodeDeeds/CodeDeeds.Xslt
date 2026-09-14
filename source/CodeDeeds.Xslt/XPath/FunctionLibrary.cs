namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Resolves a function name against the libraries this engine implements, without a parser to hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dispatch used to live in <see cref="XPathParser"/>, which was the only place it was needed: a call
    /// is written down, and what it means is settled where it is read. <c>fn:function-lookup</c> asks the same
    /// question from the other end — a name and an arity that are values, arriving while the transformation
    /// runs — so the answer had to be reachable from somewhere that is not mid-parse.
    /// </para>
    /// <para>
    /// Everything it needs is passed in, and all of it is a value: the two versions and the legacy flag. That
    /// is what makes it safe to call later. Nothing here reads the stylesheet, so nothing here goes stale, and
    /// a compiled stylesheet that keeps a reference to this class keeps a reference to no state at all.
    /// </para>
    /// <para>
    /// The functions XSLT adds — <c>key</c>, <c>document</c>, <c>system-property</c> and the rest — are
    /// deliberately not here. Each is built against what was in scope where it was written, which is exactly
    /// the thing this class does not have; see <see cref="IXPathStaticContext.TryCreateFunction"/>, which the
    /// caller asks first.
    /// </para>
    /// <para>
    /// <c>fn:id</c>, <c>fn:idref</c> and <c>fn:element-with-id</c> are the exception, and belong here rather
    /// than among those: they are XPath's own functions and need nothing from the stylesheet, only a node to
    /// start from. They had been built by the compiler alone, which meant an expression written outside a
    /// stylesheet could not call one at all — <c>id((), ())</c> was not a wrongly typed call but no call, and
    /// answered <c>XPST0017</c> where the specification asks for a type error.
    /// </para>
    /// </remarks>
    internal static class FunctionLibrary
    {
        /// <summary>
        /// Creates a call to a library function, or returns <see langword="null"/> where no library has the
        /// name.
        /// </summary>
        /// <remarks>
        /// Null means "not one of ours" and an exception means "ours, and this call is wrong" — the arity is
        /// wrong, the type has no constructor. A caller reading a written call turns the first into
        /// <c>XPST0017</c>; <c>fn:function-lookup</c> turns both into the empty sequence, which is what that
        /// function is defined to give for a name it cannot find.
        /// </remarks>
        /// <param name="namespaceUri">The name's namespace, already resolved.</param>
        /// <param name="localName">The name's local part.</param>
        /// <param name="arguments">The argument expressions.</param>
        /// <param name="version">The version in force where the call was written, which decides behaviour.</param>
        /// <param name="syntaxVersion">The version whose grammar and library are being read.</param>
        /// <param name="legacySyntax">Whether the 1.0 library alone is in force.</param>
        public static Expr? TryCreate(
            string namespaceUri,
            string localName,
            Expr[] arguments,
            XsltVersion version,
            XsltVersion syntaxVersion,
            bool legacySyntax,
            IReadOnlyDictionary<string, string>? namespaces = null,
            string defaultElementNamespace = "")
        {
            if (namespaceUri == MapArrayFunctionExpr.MapNamespace
                || namespaceUri == MapArrayFunctionExpr.ArrayNamespace)
            {
                return MapArrayFunctionExpr.Create(namespaceUri, localName, arguments);
            }

            if (namespaceUri == Xpath30FunctionExpr.MathNamespace)
            {
                return Xpath30FunctionExpr.CreateMath(localName, arguments, version);
            }

            if (namespaceUri == XdmType.SchemaNamespace)
            {
                return TypeConstructor(
                    localName, arguments, namespaces, syntaxVersion, defaultElementNamespace);
            }

            return namespaceUri.Length == 0 || namespaceUri == XdmType.FunctionNamespace
                ? Core(localName, arguments, version, syntaxVersion, legacySyntax)
                : null;
        }

        /// <summary>
        /// Creates a call to a function of the core library, in the order the libraries are consulted.
        /// </summary>
        /// <remarks>
        /// The higher-order functions come first because two of their names — <c>for-each</c> and
        /// <c>filter</c> — would otherwise have to be absent from 3.0 to stay available as extension names in
        /// 2.0. <c>fn:format-number</c> is not here: it is XSLT's function before it is XPath's, and the
        /// caller answers it before asking.
        /// </remarks>
        private static Expr? Core(
            string localName,
            Expr[] arguments,
            XsltVersion version,
            XsltVersion syntaxVersion,
            bool legacySyntax)
        {
            bool thirty = version.CompareTo(XsltVersion.V30) >= 0
                || syntaxVersion.CompareTo(XsltVersion.V30) >= 0;

            if (thirty && HigherOrderFunctionExpr.TryCreate(localName, arguments) is Expr applied)
            {
                return applied;
            }

            if (thirty && Xpath30FunctionExpr.TryCreate(localName, arguments, version) is Expr later)
            {
                return later;
            }

            if (thirty && JsonFunctionExpr.TryCreate(localName, arguments, version) is Expr json)
            {
                return json;
            }

            if (thirty && NodeBuildingFunctionExpr.TryCreate(localName, arguments, version) is Expr built)
            {
                return built;
            }

            // Before the 2.0 library and outside the version gates, because the compiler built these at any
            // version and this only moves where they are built rather than when they may be called: XPath
            // 1.0 has fn:id, and a 1.0 stylesheet has always been able to write it.
            if (localName is "id" or "idref" or "element-with-id")
            {
                if (arguments.Length is not (1 or 2))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"Function '{localName}()' expects 1 or 2 arguments, but {arguments.Length} were "
                        + "supplied.");
                }

                Expr? within = arguments.Length == 2 ? arguments[1] : null;

                return localName == "idref"
                    ? new Compiler.IdrefExpr(arguments[0], within)
                    : new Compiler.IdExpr(localName, arguments[0], within);
            }

            if (!legacySyntax
                && Xpath2FunctionExpr.TryCreate(localName, arguments, version, syntaxVersion) is Expr added)
            {
                return added;
            }

            // Last, and the only one that throws rather than declining: a name the core library does not have
            // is a name no library has, so this is where "unknown function" is decided.
            return FunctionCallExpr.IsKnown(localName)
                ? FunctionCallExpr.Create(localName, arguments, version)
                : null;
        }

        /// <summary>
        /// Creates a call to a type's constructor function, <c>xs:integer(…)</c> and its like.
        /// </summary>
        /// <remarks>
        /// A name in the XML Schema namespace is not a function anyone declared but the type itself, used to
        /// cast. The <c>xs:QName</c> constructor over a literal is the one that cannot be built here: a prefix
        /// in the literal means whatever it meant where it was written, and this class does not know that, so
        /// the caller resolves it before asking.
        /// </remarks>
        /// <param name="localName">The type's local name.</param>
        /// <param name="arguments">The argument expressions.</param>
        private static Expr TypeConstructor(
            string localName,
            Expr[] arguments,
            IReadOnlyDictionary<string, string>? namespaces,
            XsltVersion syntaxVersion,
            string defaultElementNamespace)
        {
            // A few schema types have no constructor function, and the specification names them: the abstract
            // xs:NOTATION, and the three at the top of the hierarchy that stand for 'any of these' rather than
            // for a set of values. There is no function of that name, which is what XPST0017 says — a
            // different complaint from a type this engine simply has not implemented.
            if (!XdmType.HasConstructor(localName))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"There is no constructor function 'xs:{localName}()'. The specification gives that "
                    + "type none, so there is no function of the name to call.");
            }

            // The three list types have constructors of their own name too. A cast is what a constructor
            // is, and the cast to one is already defined here -- split the text and cast each token -- so
            // the function is that cast reached by the other spelling, and fn:function-available() may say
            // so. They are looked for second because a list type is not an item type: nothing may be an
            // instance of one, which is why they are kept out of the table the rest come from.
            if (!XdmType.TryGet(localName, out XdmType.BuiltInType type)
                && !XdmType.TryGetList(localName, out type))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0051, $"'xs:{localName}' is not a type this engine can construct.");
            }

            if (arguments.Length != 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"A type constructor takes exactly one argument, but 'xs:{localName}()' was given "
                    + $"{arguments.Length}.");
            }

            // The bindings travel with the call so that xs:QName() of a computed string can resolve a prefix
            // against the namespaces where the call was written, which is where a prefix means anything.
            XdmType.RequireLiteralNameBelowThree(type, arguments[0], syntaxVersion);

            return new TypeConstructorExpr(arguments[0], type, namespaces, defaultElementNamespace);
        }
    }
}
