using System.Globalization;

namespace CodeDeeds.Xslt
{
    /// <summary>
    /// The version of XSLT a stylesheet, or a part of one, is written against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XSLT makes the version a property of an <em>element</em>, not of a stylesheet: <c>version</c> on
    /// <c>xsl:stylesheet</c> and <c>xsl:version</c> on a literal result element both set it for everything
    /// below them, and either may be overridden further down. So this is resolved per element rather than once
    /// per module.
    /// </para>
    /// <para>
    /// Two behaviours hang off it, in opposite directions. Where the version named is <em>later</em> than this
    /// engine implements, forwards-compatible processing applies: an unrecognised instruction becomes
    /// something to fall back from at run time rather than a reason to reject the stylesheet. Where it is
    /// <em>earlier</em>, backwards-compatible behaviour applies: the constructs that XSLT 2.0 redefined —
    /// <c>xsl:value-of</c> taking a whole sequence rather than its first item, comparison operators converting
    /// their operands to numbers — keep their 1.0 meaning. Both are decided when the stylesheet is compiled,
    /// so neither costs anything while it runs.
    /// </para>
    /// </remarks>
    public readonly struct XsltVersion : IEquatable<XsltVersion>, IComparable<XsltVersion>
    {
        private readonly int m_hundredths;

        private XsltVersion(int hundredths)
        {
            m_hundredths = hundredths;
        }

        /// <summary>XSLT 1.0.</summary>
        public static XsltVersion V10 => new XsltVersion(100);

        /// <summary>XSLT 2.0.</summary>
        public static XsltVersion V20 => new XsltVersion(200);

        /// <summary>XSLT 3.0.</summary>
        public static XsltVersion V30 => new XsltVersion(300);

        /// <summary>
        /// The most recent version this engine implements, which is what a caller naming none gets. A
        /// stylesheet naming a later version is processed forwards-compatibly rather than rejected.
        /// </summary>
        /// <remarks>
        /// 3.0 since the XSLT 3.0 suite passed at 99.5% and the 3.1 function library was complete but for
        /// <c>fn:load-xquery-module</c>. It had been 2.0 while the 3.0 work was reached by asking for it,
        /// so that a <c>version="3.0"</c> stylesheet with an <c>xsl:fallback</c> beside an instruction this
        /// engine lacked kept the fallback. A caller who wants a 2.0 processor still has one, by setting
        /// <see cref="XsltOptions.Version"/> to <see cref="V20"/>.
        /// </remarks>
        public static XsltVersion Implemented => V30;

        /// <summary>Gets the version as it is reported by <c>system-property('xsl:version')</c>.</summary>
        public double Number => m_hundredths / 100.0;

        /// <summary>
        /// Gets whether a stylesheet written for this version needs the XSLT 1.0 meaning of the constructs that
        /// XSLT 2.0 redefined.
        /// </summary>
        public bool IsBackwardsCompatible => m_hundredths < 200;

        /// <summary>
        /// Parses the value of a <c>version</c> or <c>xsl:version</c> attribute.
        /// </summary>
        /// <remarks>
        /// A value that is not a number is treated as a later version than this engine knows, which puts the
        /// stylesheet into forwards-compatible processing. That is the more useful reading of a version this
        /// engine cannot make sense of: it will run what it understands rather than refusing outright.
        /// <para>
        /// Read as a decimal, which is the type the attribute has, and truncated rather than rounded to the
        /// hundredth. <c>1.999999999</c> is a version below 2.0 and has to stay one — rounding it to the
        /// nearest hundredth makes it exactly 2.0 and turns backwards compatible processing off, which is
        /// the opposite of what a stylesheet writing that number is asking for.
        /// </para>
        /// </remarks>
        /// <param name="text">The attribute value.</param>
        /// <returns>The version named.</returns>
        public static XsltVersion Parse(string text)
        {
            if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
            {
                return new XsltVersion(int.MaxValue);
            }

            decimal hundredths = decimal.Floor(value * 100m);

            return new XsltVersion(hundredths >= int.MaxValue
                ? int.MaxValue
                : hundredths <= 0 ? 0 : (int)hundredths);
        }

        /// <inheritdoc/>
        public bool Equals(XsltVersion other) => m_hundredths == other.m_hundredths;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is XsltVersion other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => m_hundredths;

        /// <inheritdoc/>
        public int CompareTo(XsltVersion other) => m_hundredths.CompareTo(other.m_hundredths);

        /// <inheritdoc/>
        public override string ToString()
        {
            return m_hundredths == int.MaxValue
                ? "(unrecognised)"
                : Number.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>Compares two versions.</summary>
        public static bool operator <(XsltVersion left, XsltVersion right) => left.m_hundredths < right.m_hundredths;

        /// <summary>Compares two versions.</summary>
        public static bool operator >(XsltVersion left, XsltVersion right) => left.m_hundredths > right.m_hundredths;

        /// <summary>Compares two versions.</summary>
        public static bool operator <=(XsltVersion left, XsltVersion right) => left.m_hundredths <= right.m_hundredths;

        /// <summary>Compares two versions.</summary>
        public static bool operator >=(XsltVersion left, XsltVersion right) => left.m_hundredths >= right.m_hundredths;

        /// <summary>Compares two versions.</summary>
        public static bool operator ==(XsltVersion left, XsltVersion right) => left.Equals(right);

        /// <summary>Compares two versions.</summary>
        public static bool operator !=(XsltVersion left, XsltVersion right) => !left.Equals(right);
    }
}
