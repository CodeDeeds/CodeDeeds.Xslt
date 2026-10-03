using System.Text.RegularExpressions;

namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// Words added to what .NET's schema validator says, where the validator is the cause rather than the
    /// document.
    /// </summary>
    internal static partial class SchemaMessages
    {
        // A value that begins with a negative year, or with a year of more than four digits: the shapes of
        // xs:date, xs:dateTime, xs:gYear and xs:gYearMonth that XSD 1.0 allows and System.DateTime cannot hold.
        [GeneratedRegex(@"The value '(?:-\d{4,}|[1-9]\d{4,})(?:-|Z|\+|:|T|'|$)")]
        private static partial Regex OutOfRangeYear();

        /// <summary>
        /// Adds to a validator message that is about a date outside the years <see cref="DateTime"/> holds
        /// (1 to 9999) that the value is one XSD allows and that this validator cannot hold, so that the
        /// reader is not left looking for a fault in the document.
        /// </summary>
        /// <param name="message">What the validator said.</param>
        /// <returns>The message, with the explanation after it where it applies.</returns>
        /// <example>
        /// <c>-0012-12-03-05:00</c> as an <c>xs:date</c> is refused with the validator's "not a valid Date
        /// value", followed by a note that the year is outside what the validator holds.
        /// </example>
        public static string Explain(string message)
        {
            if (message.Contains("is not a valid", StringComparison.Ordinal) || message.Contains("datatype", StringComparison.Ordinal))
            {
                if (OutOfRangeYear().IsMatch(message))
                {
                    return message
                        + " (The value may well be valid XSD: the validator is .NET's, which holds a date in"
                        + " System.DateTime and so accepts only the years 1 to 9999. A negative year, or one"
                        + " past 9999, is refused by it although this engine's own dates reach them.)";
                }
            }

            return message;
        }
    }
}
