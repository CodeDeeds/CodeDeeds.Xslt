using System.Globalization;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Which code points belong to which Unicode general category, over the whole of Unicode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// .NET answers this one code point at a time and matches one UTF-16 unit at a time, so its own
    /// <c>\p{Lu}</c> cannot see an uppercase letter above the basic plane: the two units that spell it are
    /// both <c>Cs</c>, and neither is a letter. Reading the categories out into ranges is what lets a
    /// category escape be turned into a set that can then be spelled as surrogate pairs.
    /// </para>
    /// <para>
    /// The table is read from <see cref="CharUnicodeInfo"/> rather than written down here, so it says
    /// whatever the running framework's Unicode version says and cannot drift from the rest of the engine.
    /// The cost of that is one pass over all 1,114,112 code points — about 4 ms, once per process, and only
    /// for a process whose stylesheet uses a category escape at all. A pattern with no <c>\p</c>, <c>\d</c>
    /// or <c>\w</c> in it never asks.
    /// </para>
    /// </remarks>
    internal static class UnicodeCategories
    {
        /// <summary>How many bits of a boundary hold the category, the rest holding the code point.</summary>
        private const int CategoryBits = 5;

        private const uint CategoryMask = (1u << CategoryBits) - 1;

        private static readonly Lazy<uint[]> s_boundaries =
            new Lazy<uint[]>(Scan, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// The categories a name stands for, as a bit per <see cref="UnicodeCategory"/>, or null for a name
        /// that is not one.
        /// </summary>
        /// <remarks>
        /// A name is one letter naming a group or two naming a category within it, which is the whole of
        /// XML Schema's grammar for this. <c>Cs</c> is deliberately not among them: it is the surrogates,
        /// which spell characters rather than being any, and Schema's <c>C</c> does not offer it.
        /// </remarks>
        public static uint? Mask(string name)
        {
            return name switch
            {
                "L" => Bits(
                    UnicodeCategory.UppercaseLetter,
                    UnicodeCategory.LowercaseLetter,
                    UnicodeCategory.TitlecaseLetter,
                    UnicodeCategory.ModifierLetter,
                    UnicodeCategory.OtherLetter),
                "Lu" => Bits(UnicodeCategory.UppercaseLetter),
                "Ll" => Bits(UnicodeCategory.LowercaseLetter),
                "Lt" => Bits(UnicodeCategory.TitlecaseLetter),
                "Lm" => Bits(UnicodeCategory.ModifierLetter),
                "Lo" => Bits(UnicodeCategory.OtherLetter),

                "M" => Bits(
                    UnicodeCategory.NonSpacingMark,
                    UnicodeCategory.SpacingCombiningMark,
                    UnicodeCategory.EnclosingMark),
                "Mn" => Bits(UnicodeCategory.NonSpacingMark),
                "Mc" => Bits(UnicodeCategory.SpacingCombiningMark),
                "Me" => Bits(UnicodeCategory.EnclosingMark),

                "N" => Bits(
                    UnicodeCategory.DecimalDigitNumber,
                    UnicodeCategory.LetterNumber,
                    UnicodeCategory.OtherNumber),
                "Nd" => Bits(UnicodeCategory.DecimalDigitNumber),
                "Nl" => Bits(UnicodeCategory.LetterNumber),
                "No" => Bits(UnicodeCategory.OtherNumber),

                "P" => Bits(
                    UnicodeCategory.ConnectorPunctuation,
                    UnicodeCategory.DashPunctuation,
                    UnicodeCategory.OpenPunctuation,
                    UnicodeCategory.ClosePunctuation,
                    UnicodeCategory.InitialQuotePunctuation,
                    UnicodeCategory.FinalQuotePunctuation,
                    UnicodeCategory.OtherPunctuation),
                "Pc" => Bits(UnicodeCategory.ConnectorPunctuation),
                "Pd" => Bits(UnicodeCategory.DashPunctuation),
                "Ps" => Bits(UnicodeCategory.OpenPunctuation),
                "Pe" => Bits(UnicodeCategory.ClosePunctuation),
                "Pi" => Bits(UnicodeCategory.InitialQuotePunctuation),
                "Pf" => Bits(UnicodeCategory.FinalQuotePunctuation),
                "Po" => Bits(UnicodeCategory.OtherPunctuation),

                "Z" => Bits(
                    UnicodeCategory.SpaceSeparator,
                    UnicodeCategory.LineSeparator,
                    UnicodeCategory.ParagraphSeparator),
                "Zs" => Bits(UnicodeCategory.SpaceSeparator),
                "Zl" => Bits(UnicodeCategory.LineSeparator),
                "Zp" => Bits(UnicodeCategory.ParagraphSeparator),

                "S" => Bits(
                    UnicodeCategory.MathSymbol,
                    UnicodeCategory.CurrencySymbol,
                    UnicodeCategory.ModifierSymbol,
                    UnicodeCategory.OtherSymbol),
                "Sm" => Bits(UnicodeCategory.MathSymbol),
                "Sc" => Bits(UnicodeCategory.CurrencySymbol),
                "Sk" => Bits(UnicodeCategory.ModifierSymbol),
                "So" => Bits(UnicodeCategory.OtherSymbol),

                "C" => Bits(
                    UnicodeCategory.Control,
                    UnicodeCategory.Format,
                    UnicodeCategory.PrivateUse,
                    UnicodeCategory.OtherNotAssigned),
                "Cc" => Bits(UnicodeCategory.Control),
                "Cf" => Bits(UnicodeCategory.Format),
                "Co" => Bits(UnicodeCategory.PrivateUse),
                "Cn" => Bits(UnicodeCategory.OtherNotAssigned),

                _ => null,
            };
        }

        /// <summary>Adds every code point in any of the named categories to a set.</summary>
        /// <param name="set">The set to add to.</param>
        /// <param name="categories">A bit per category, as <see cref="Mask"/> returns.</param>
        public static void AddTo(CodePointSet set, uint categories)
        {
            uint[] boundaries = s_boundaries.Value;

            for (int i = 0; i < boundaries.Length; i++)
            {
                if ((categories & (1u << (int)(boundaries[i] & CategoryMask))) == 0)
                {
                    continue;
                }

                int low = (int)(boundaries[i] >> CategoryBits);
                int high = i + 1 < boundaries.Length
                    ? (int)(boundaries[i + 1] >> CategoryBits) - 1
                    : CodePointSet.Max;

                set.Add(low, high);
            }
        }

        private static uint Bits(params UnicodeCategory[] categories)
        {
            uint mask = 0;

            foreach (UnicodeCategory category in categories)
            {
                mask |= 1u << (int)category;
            }

            return mask;
        }

        /// <summary>
        /// Walks every code point once, recording where the category changes.
        /// </summary>
        /// <remarks>
        /// The result is about 1,300 boundaries, each holding the code point a run starts at and the
        /// category it runs in — so a run ends where the next one begins, and the last runs to the end.
        /// Storing the boundaries rather than a range per category is what keeps this to one array: the
        /// runs of every category, unassigned included, tile the whole of Unicode exactly once.
        /// </remarks>
        private static uint[] Scan()
        {
            List<uint> boundaries = new List<uint>(1400);
            UnicodeCategory run = CharUnicodeInfo.GetUnicodeCategory(0);
            boundaries.Add((uint)run);

            for (int code = 1; code <= CodePointSet.Max; code++)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(code);

                if (category != run)
                {
                    run = category;
                    boundaries.Add(((uint)code << CategoryBits) | (uint)category);
                }
            }

            return boundaries.ToArray();
        }
    }
}
