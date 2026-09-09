using System.Xml.Linq;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Puts a result fragment into a canonical form so that two processors can be compared on what they
    /// produced rather than on how they chose to write it.
    /// </summary>
    /// <remarks>
    /// Re-parsing already settles self-closing style and entity forms. Attribute order is settled here too:
    /// XML gives it no meaning, and processors legitimately differ — the framework's writer emits a namespace
    /// declaration at the point the prefix is first needed, whereas this engine declares it with the element
    /// that owns it. Comparing that would be comparing formatting, not behaviour.
    /// </remarks>
    internal static class XmlComparison
    {
        /// <summary>Canonicalizes a result fragment for comparison.</summary>
        /// <param name="fragment">The fragment, which need not have a single root element.</param>
        public static string Normalize(string fragment)
        {
            XDocument document;

            try
            {
                document = XDocument.Parse(
                    $"<harness-wrapper>{fragment}</harness-wrapper>", LoadOptions.PreserveWhitespace);
            }
            catch (System.Xml.XmlException)
            {
                // HTML output is deliberately not well-formed XML — a void element is left unclosed — so it
                // cannot be re-parsed. Comparing it verbatim is the stricter check anyway; the leniency here
                // exists only to absorb formatting choices, and there are none to absorb if this fails.
                return fragment;
            }

            SortAttributes(document.Root!);
            return document.ToString(SaveOptions.DisableFormatting);
        }

        private static void SortAttributes(XElement element)
        {
            XAttribute[] sorted = element.Attributes()
                .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal)
                .ToArray();

            element.RemoveAttributes();
            foreach (XAttribute attribute in sorted)
            {
                element.Add(attribute);
            }

            foreach (XElement child in element.Elements())
            {
                SortAttributes(child);
            }
        }
    }
}
