using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A map, which XPath 3.1 adds: an unordered collection of key-value pairs, where a key is an atomic
    /// value and a value is any sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A map is an <em>item</em> but not an atomic value, which is what separates it from everything
    /// <see cref="XdmTypeCode"/> lists. It has no string-value and cannot be atomized: asking for one is an
    /// error rather than a rendering, because a map is a structure and there is no text that means the same.
    /// </para>
    /// <para>
    /// Immutable. <c>map:put</c> returns a new map rather than changing this one, which is what lets a map be
    /// a value: two expressions holding the same map cannot see each other's writes, and a map in a global
    /// variable is safe to share between threads for the same reason a compiled stylesheet is.
    /// </para>
    /// </remarks>
    public sealed class XdmMap
    {
        private readonly Dictionary<XdmKey, XPathValue> m_entries;

        private XdmMap(Dictionary<XdmKey, XPathValue> entries)
        {
            m_entries = entries;
        }

        /// <summary>The map with no entries.</summary>
        public static XdmMap Empty { get; } = new XdmMap(new Dictionary<XdmKey, XPathValue>());

        /// <summary>Gets how many entries the map has.</summary>
        public int Count => m_entries.Count;

        /// <summary>Gets the keys, in no particular order — a map is unordered.</summary>
        public IEnumerable<XPathValue> Keys
        {
            get
            {
                foreach (XdmKey key in m_entries.Keys)
                {
                    yield return key.Value;
                }
            }
        }

        /// <summary>Gets the entries, in no particular order.</summary>
        public IEnumerable<KeyValuePair<XPathValue, XPathValue>> Entries
        {
            get
            {
                foreach (KeyValuePair<XdmKey, XPathValue> entry in m_entries)
                {
                    yield return new KeyValuePair<XPathValue, XPathValue>(entry.Key.Value, entry.Value);
                }
            }
        }

        /// <summary>Builds a map of one entry.</summary>
        /// <param name="key">The key, which must be a single atomic value.</param>
        /// <param name="value">The value, which may be any sequence.</param>
        public static XdmMap Entry(XPathValue key, XPathValue value)
        {
            return new XdmMap(new Dictionary<XdmKey, XPathValue> { [XdmKey.Of(key)] = value });
        }

        /// <summary>
        /// Builds a map from entries, deciding what to do when two of them share a key.
        /// </summary>
        /// <param name="entries">The entries, in the order they were written.</param>
        /// <param name="duplicates">
        /// What a repeated key means: <c>use-first</c>, <c>use-last</c>, <c>use-any</c>, <c>combine</c> or
        /// <c>reject</c>, as <c>map:merge</c> names them.
        /// </param>
        /// <exception cref="XsltException">A key repeats and <c>reject</c> was asked for.</exception>
        public static XdmMap Build(
            IEnumerable<KeyValuePair<XPathValue, XPathValue>> entries,
            string duplicates = "use-first",
            XsltErrorCode rejection = XsltErrorCode.FOJS0003)
        {
            Dictionary<XdmKey, XPathValue> built = new Dictionary<XdmKey, XPathValue>();

            foreach (KeyValuePair<XPathValue, XPathValue> entry in entries)
            {
                XdmKey key = XdmKey.Of(entry.Key);

                if (!built.TryGetValue(key, out XPathValue existing))
                {
                    built[key] = entry.Value;
                    continue;
                }

                switch (duplicates)
                {
                    case "use-last":
                        built[key] = entry.Value;
                        break;

                    case "combine":
                    {
                        List<XPathValue> items = XdmSequence.Items(existing);
                        items.AddRange(XdmSequence.Items(entry.Value));
                        built[key] = XdmSequence.Concatenate(items);
                        break;
                    }

                    case "reject":
                        throw XsltErrors.Error(
                            rejection,
                            $"The key '{XdmSequence.StringValueOf(entry.Key)}' appears more than once, and "
                            + "duplicates were rejected.");

                    default:
                        // use-first and use-any: the one already there stays. "any" permits either, and
                        // choosing the first makes the result depend on nothing but the input.
                        break;
                }
            }

            return built.Count == 0 ? Empty : new XdmMap(built);
        }

        /// <summary>Returns the value a key maps to, or the empty sequence where the key is absent.</summary>
        /// <param name="key">The key to look up.</param>
        public XPathValue Get(XPathValue key)
        {
            return m_entries.TryGetValue(XdmKey.Of(key), out XPathValue found)
                ? found
                : XPathValue.FromSequence(XdmSequence.Empty);
        }

        /// <summary>Returns whether a key is present, which is not the same as its value being non-empty.</summary>
        /// <param name="key">The key to look for.</param>
        public bool Contains(XPathValue key) => m_entries.ContainsKey(XdmKey.Of(key));

        /// <summary>Returns this map with one entry added or replaced.</summary>
        /// <param name="key">The key to bind.</param>
        /// <param name="value">What to bind it to.</param>
        public XdmMap Put(XPathValue key, XPathValue value)
        {
            Dictionary<XdmKey, XPathValue> copy = new Dictionary<XdmKey, XPathValue>(m_entries)
            {
                [XdmKey.Of(key)] = value,
            };

            return new XdmMap(copy);
        }

        /// <summary>Returns this map without the given keys, ignoring any that are not present.</summary>
        /// <param name="keys">The keys to drop.</param>
        public XdmMap Remove(IEnumerable<XPathValue> keys)
        {
            Dictionary<XdmKey, XPathValue> copy = new Dictionary<XdmKey, XPathValue>(m_entries);

            foreach (XPathValue key in keys)
            {
                copy.Remove(XdmKey.Of(key));
            }

            return copy.Count == 0 ? Empty : new XdmMap(copy);
        }
    }

    /// <summary>
    /// An atomic value taken as a map key, which is a coarser thing than the value itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 3.1 compares keys with <c>op:same-key</c> rather than with <c>eq</c>, and the two differ in
    /// three ways that matter here. <c>NaN</c> is the same key as <c>NaN</c>, where <c>eq</c> says it equals
    /// nothing including itself — otherwise a map could hold an entry nobody could ever read back.
    /// <c>xs:string</c>, <c>xs:anyURI</c> and <c>xs:untypedAtomic</c> are one family, so the text decides.
    /// And the numeric types are one family, so <c>1</c>, <c>1.0</c> and <c>1e0</c> are one key.
    /// </para>
    /// <para>
    /// A number that is a whole number is keyed as an integer rather than as a double, because this engine
    /// carries eighteen digits of <c>xs:integer</c> and a double carries fifteen: keying by the double would
    /// quietly merge two integers that are not equal.
    /// </para>
    /// <para>
    /// Everything else is keyed by its type together with its canonical form, which gives the specification's
    /// answer for dates and durations without a comparison rule per type: a value with a timezone writes
    /// differently from one without, and so is a different key, which is what the specification says.
    /// </para>
    /// </remarks>
    internal readonly struct XdmKey : IEquatable<XdmKey>
    {
        private enum Family : byte
        {
            Text = 0,
            Integral = 1,
            Real = 2,
            Boolean = 3,
            Other = 4,
        }

        private readonly Family m_family;
        private readonly string? m_text;
        private readonly long m_integral;
        private readonly double m_real;

        private XdmKey(Family family, XPathValue value, string? text, long integral, double real)
        {
            m_family = family;
            Value = value;
            m_text = text;
            m_integral = integral;
            m_real = real;
        }

        /// <summary>Gets the value this key was made from, which is what <c>map:keys</c> gives back.</summary>
        public XPathValue Value { get; }

        /// <summary>Takes an atomic value as a key.</summary>
        /// <param name="value">The value, which must be a single atomic value.</param>
        /// <exception cref="XsltException">The value is a node, a sequence, a map or an array.</exception>
        public static XdmKey Of(XPathValue value)
        {
            // A node is atomized first, which is what lets an attribute out of the input be a key. In a
            // tree nothing validated its typed value is its text as xs:untypedAtomic — and that shares a
            // key family with xs:string, so map { @id: 1 } is read back by the text of the id.
            if (value.Kind is XPathValueKind.Node or XPathValueKind.NodeSet)
            {
                value = XdmSequence.TypedValueAsOne(value, "A map key");
            }

            switch (value.Kind)
            {
                case XPathValueKind.String:
                    return value.TypeCode == XdmTypeCode.String
                        || value.TypeCode == XdmTypeCode.AnyUri
                        || value.TypeCode == XdmTypeCode.UntypedAtomic
                            ? new XdmKey(Family.Text, value, value.ToStringValue(), 0L, 0.0)
                            : new XdmKey(Family.Other, value, Tagged(value), 0L, 0.0);

                case XPathValueKind.Boolean:
                    return new XdmKey(Family.Boolean, value, null, value.ToBoolean() ? 1L : 0L, 0.0);

                case XPathValueKind.Number:
                {
                    double number = value.ToNumber();

                    // A whole number keys as an integer whatever type wrote it, so 1, 1.0 and 1e0 meet.
                    if (value.TypeCode == XdmTypeCode.Integer && !value.IsWideInteger)
                    {
                        return new XdmKey(Family.Integral, value, null, value.ToInteger(), 0.0);
                    }

                    if (!double.IsNaN(number)
                        && !double.IsInfinity(number)
                        && number == Math.Floor(number)
                        && number >= long.MinValue
                        && number <= long.MaxValue)
                    {
                        return new XdmKey(Family.Integral, value, null, (long)number, 0.0);
                    }

                    // A whole number with no 64-bit form keys by its digits rather than by the double
                    // it approximates to. Past 2^53 a double stops telling consecutive integers apart,
                    // so keying by one would put every pair of wide integers in the same bucket — and
                    // a map has to keep the keys its own equality says are different.
                    if (value.IsWideInteger)
                    {
                        return new XdmKey(
                            Family.Text,
                            value,
                            "i:" + value.ToBigInteger().ToString(CultureInfo.InvariantCulture),
                            0L,
                            0.0);
                    }

                    // A decimal that no double names exactly keys by its own digits. op:same-key compares
                    // numeric keys by what they are worth and promotes neither to the other's type, so
                    // xs:decimal('1.0000000000100000000001') and xs:double('1.00000000001') are two keys
                    // although the first converts to exactly the second — the suite's map-put-023, whose
                    // sibling map-remove-016 says outright that a decimal is not promoted to a double.
                    // A decimal that a double does name exactly keys as that double, which is what makes
                    // 1.5 and 1.5e0 one key.
                    if (value.TypeCode == XdmTypeCode.Decimal && !NamesExactly(number, value.ToDecimal()))
                    {
                        return new XdmKey(Family.Text, value, "x:" + value.ToStringValue(), 0L, 0.0);
                    }

                    return new XdmKey(Family.Real, value, null, 0L, number);
                }

                default:
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        "A map key has to be a single atomic value, and this is "
                        + Describe(value.Kind) + ".");
            }
        }

        /// <summary>
        /// Whether a double is worth exactly what a decimal is worth, neither rounded to the other.
        /// </summary>
        /// <remarks>
        /// Both are exact numbers written in different bases: a decimal is <c>n / 10^s</c> and a double is
        /// <c>m * 2^e</c>, so they are the same number exactly when <c>n * 2^-e = m * 10^s</c>, which whole
        /// numbers answer with nothing rounded. Converting one to the other and comparing would answer a
        /// different question, which is the one that put two keys in one bucket.
        /// </remarks>
        /// <param name="approximation">The double, which is the decimal converted.</param>
        /// <param name="exact">The decimal.</param>
        private static bool NamesExactly(double approximation, decimal exact)
        {
            if (double.IsNaN(approximation) || double.IsInfinity(approximation))
            {
                return false;
            }

            int[] parts = decimal.GetBits(exact);
            int scale = (parts[3] >> 16) & 0xFF;
            System.Numerics.BigInteger digits =
                (new System.Numerics.BigInteger((uint)parts[2]) << 64)
                + (new System.Numerics.BigInteger((uint)parts[1]) << 32)
                + (uint)parts[0];

            if ((parts[3] & unchecked((int)0x80000000)) != 0)
            {
                digits = -digits;
            }

            long bits = BitConverter.DoubleToInt64Bits(approximation);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            int exponent = (int)((bits >> 52) & 0x7FF);

            // A subnormal has no hidden bit and the exponent one step up from what the field says.
            if (exponent == 0)
            {
                exponent = 1;
            }
            else
            {
                mantissa |= 1L << 52;
            }

            exponent -= 1075;
            System.Numerics.BigInteger binary = bits < 0 ? -mantissa : mantissa;

            System.Numerics.BigInteger left = digits;
            System.Numerics.BigInteger right = binary * System.Numerics.BigInteger.Pow(10, scale);

            if (exponent >= 0)
            {
                right *= System.Numerics.BigInteger.Pow(2, exponent);
            }
            else
            {
                left *= System.Numerics.BigInteger.Pow(2, -exponent);
            }

            return left == right;
        }

        /// <summary>The type together with the text, so a date is never the same key as a string of it.</summary>
        /// <remarks>
        /// A date or a time carrying a timezone is tagged by the moment it names rather than by the way it
        /// was written, two keys being the same key when <c>eq</c> says so and <c>eq</c> comparing these as
        /// moments: <c>18:15:00-05:00</c> and <c>23:15:00Z</c> are one time and so one key.
        /// <para>
        /// One with no timezone is tagged by what it says instead, and so is never the same key as one that
        /// has a timezone — however the implicit timezone would settle it. That is the deliberate answer,
        /// the alternative being a map whose keys collide or not according to a setting outside it, and the
        /// suite pins it: maps-010 puts <c>current-dateTime()</c> and the same moment with the timezone
        /// taken off into one map and asks for a size of two.
        /// </para>
        /// </remarks>
        private static string Tagged(XPathValue value)
        {
            // A duration is tagged by what it measures rather than by how it was written or which of the
            // three types wrote it: xs:duration('P1Y') and xs:yearMonthDuration('P12M') are twelve months
            // and no seconds either way, so they are one key. The two subtypes share the tag with the
            // supertype for the same reason — nothing about a key depends on which name a value came under.
            if (value.TypeCode
                is XdmTypeCode.Duration or XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration)
            {
                XdmDuration duration = value.AsDuration();

                return "d:" + duration.Months + ":"
                    + duration.Seconds.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TypeCode is XdmTypeCode.Date or XdmTypeCode.Time or XdmTypeCode.DateTime)
            {
                return (int)value.TypeCode + ":" + value.AsDateTime().Key;
            }

            // A name is its namespace and its local part. The canonical form of one is the way it was
            // written, prefix and all, and two names written the same way in two namespaces are two names:
            // the suite's same-key-021 puts QName((), 'abc') and QName('http://example.org', 'abc') into one
            // map and asks for both.
            if (value.TypeCode == XdmTypeCode.QName)
            {
                XdmQName name = value.AsQName();
                return (int)value.TypeCode + ":" + name.NamespaceUri + "}" + name.LocalName;
            }

            return (int)value.TypeCode + ":" + value.ToCanonicalString();
        }

        private static string Describe(XPathValueKind kind)
        {
            return kind switch
            {
                XPathValueKind.Map => "a map",
                XPathValueKind.Array => "an array",
                XPathValueKind.Node or XPathValueKind.NodeSet => "a node",
                _ => "a sequence",
            };
        }

        /// <inheritdoc/>
        public bool Equals(XdmKey other)
        {
            if (m_family != other.m_family)
            {
                return false;
            }

            return m_family switch
            {
                Family.Text or Family.Other => string.Equals(m_text, other.m_text, StringComparison.Ordinal),
                Family.Integral or Family.Boolean => m_integral == other.m_integral,

                // Bitwise, so that NaN is the same key as NaN — which eq denies and a map needs.
                _ => BitConverter.DoubleToInt64Bits(m_real) == BitConverter.DoubleToInt64Bits(other.m_real)
                    || (double.IsNaN(m_real) && double.IsNaN(other.m_real)),
            };
        }

        /// <inheritdoc/>
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is XdmKey other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return m_family switch
            {
                Family.Text or Family.Other => m_text is null ? 0 : m_text.GetHashCode(StringComparison.Ordinal),
                Family.Integral or Family.Boolean => m_integral.GetHashCode(),
                _ => double.IsNaN(m_real) ? -1 : m_real.GetHashCode(),
            };
        }
    }
}
