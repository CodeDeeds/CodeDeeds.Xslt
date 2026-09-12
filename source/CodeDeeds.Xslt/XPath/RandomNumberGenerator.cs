using System.Globalization;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A call to <c>fn:random-number-generator()</c>, which XPath 3.1 adds: a source of pseudo-random numbers
    /// that is a value rather than a side effect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result is a map of three entries. <c>number</c> is a double in [0, 1); <c>next</c> is a function
    /// of no arguments that returns the generator after this one; and <c>permute</c> is a function of one
    /// argument that returns its items in a random order. A stylesheet wanting ten numbers walks <c>next</c>
    /// ten times, which is the shape a language without assignment has to give randomness: a generator never
    /// changes, and each number is a step further along one fixed stream.
    /// </para>
    /// <para>
    /// Everything is settled by the seed. The same seed gives the same map, the same <c>next</c> and the same
    /// permutation of the same sequence, which the specification requires and which is what lets a
    /// transformation be repeated. The stream is SplitMix64's — a 64-bit state stepped by the golden-ratio
    /// increment and mixed on the way out — chosen because it passes the usual statistical batteries, is a
    /// dozen lines, and has no state beyond the one number a map entry can be built from.
    /// </para>
    /// <para>
    /// Called without a seed, the generator is seeded from the transformation's reading of the clock — the
    /// one <c>current-dateTime()</c> reports — so that every seedless call in one execution scope returns
    /// the same generator, as the specification asks of it, and two transformations return different ones.
    /// </para>
    /// </remarks>
    internal sealed class RandomNumberGeneratorExpr : Expr
    {
        private const string Name = "random-number-generator";

        private readonly Expr? m_seed;

        private RandomNumberGeneratorExpr(Expr? seed)
        {
            m_seed = seed;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_seed is null ? Array.Empty<Expr>() : new[] { m_seed };

        /// <summary>Creates the call.</summary>
        /// <param name="arguments">The compiled arguments: nothing, or the seed.</param>
        /// <param name="version">The version in force.</param>
        /// <exception cref="XsltException"><c>XPST0017</c> where there is more than one argument.</exception>
        public static Expr Create(Expr[] arguments, XsltVersion version)
        {
            if (arguments.Length > 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'{Name}()' takes at most one argument, and was given {arguments.Length}.");
            }

            Expr[] seed = CheckedArgumentExpr.Wrap(
                arguments, FunctionParameter.Parse("xs:anyAtomicType?"), Name, version);

            return new RandomNumberGeneratorExpr(seed.Length == 0 ? null : seed[0]);
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            if (m_seed is null)
            {
                return Generator(ClockSeed(ref context));
            }

            // Atomized here rather than left to the argument check, so that a map or a function handed as
            // the seed is refused the way atomizing one is refused everywhere else.
            List<XPathValue> seed = XdmSequence.Atomize(XdmSequence.Items(m_seed.Evaluate(ref context)));

            if (seed.Count == 0)
            {
                return Generator(ClockSeed(ref context));
            }

            if (seed.Count > 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"The seed of fn:{Name}() is one atomic value or none, and this is {seed.Count}.");
            }

            // The type together with the text, so that the string '1' and the integer 1 are different seeds
            // the way they are different values — and the same seed is the same text every time.
            return Generator(Hash((int)seed[0].TypeCode + ":" + seed[0].ToCanonicalString()));
        }

        /// <summary>
        /// The seed for a call that gave none: the clock reading this execution scope shares, which is fixed
        /// within it and moves between one transformation and the next.
        /// </summary>
        private static ulong ClockSeed(ref DynamicContext context)
        {
            return Hash("clock:" + context.ReadClock().Now.UtcTicks.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Builds the map for a state, which is the generator at one point along its stream.</summary>
        /// <param name="state">The state before this generator's step.</param>
        private static XPathValue Generator(ulong state)
        {
            ulong stepped = Step(state);
            ulong output = Mix(stepped);

            return XPathValue.FromMap(XdmMap.Build(new[]
            {
                Entry("number", XPathValue.FromNumber(UnitInterval(output))),
                Entry("next", XPathValue.FromFunction(new XdmNativeFunction(0, _ => Generator(stepped)))),
                Entry("permute", XPathValue.FromFunction(new XdmNativeFunction(1, arguments => Permute(arguments[0], output)))),
            }));
        }

        private static KeyValuePair<XPathValue, XPathValue> Entry(string key, XPathValue value)
        {
            return new KeyValuePair<XPathValue, XPathValue>(XPathValue.FromString(key), value);
        }

        /// <summary>
        /// Returns a sequence's items in a random order that the state decides.
        /// </summary>
        /// <remarks>
        /// Fisher–Yates from the back: each position is swapped with one chosen among those not yet settled,
        /// which gives every ordering the same chance and touches each item once. The stream it draws from
        /// starts at this generator's own output, so the same generator permutes the same sequence the same
        /// way, and two generators along one stream permute it differently.
        /// </remarks>
        private static XPathValue Permute(XPathValue sequence, ulong state)
        {
            List<XPathValue> items = XdmSequence.Items(sequence);

            for (int last = items.Count - 1; last > 0; last--)
            {
                state = Step(state);
                int chosen = (int)(Mix(state) % (ulong)(last + 1));
                (items[last], items[chosen]) = (items[chosen], items[last]);
            }

            return XdmSequence.Concatenate(items);
        }

        /// <summary>The top 53 bits as a double in [0, 1), which is every value a double can hold there evenly.</summary>
        private static double UnitInterval(ulong output)
        {
            return (output >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>SplitMix64's step: the state moves by the golden ratio, which visits every state before repeating.</summary>
        private static ulong Step(ulong state)
        {
            return unchecked(state + 0x9E3779B97F4A7C15UL);
        }

        /// <summary>SplitMix64's output mix, which scatters the bits of a state that differ little.</summary>
        private static ulong Mix(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>
        /// FNV-1a over the text, which turns a seed of any type into a state.
        /// </summary>
        /// <remarks>
        /// Written out rather than borrowed from <see cref="string.GetHashCode()"/>, which is salted per
        /// process: a seed has to give the same stream tomorrow as it gives today.
        /// </remarks>
        private static ulong Hash(string text)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;

                foreach (char c in text)
                {
                    hash = (hash ^ c) * 1099511628211UL;
                }

                return hash;
            }
        }
    }
}
