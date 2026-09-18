using System.Text;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// Turns the encoding a stylesheet declared into one that can write bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>xsl:output encoding="…"</c> can only be honoured where this engine controls the bytes, which is
    /// where the caller supplies a <see cref="Stream"/> rather than a <see cref="TextWriter"/>. Writing to a
    /// text writer, the engine has no say: the declaration says <c>ISO-8859-1</c> and the writer emits
    /// whatever it was built with, so the document makes a claim its own bytes do not support.
    /// </para>
    /// <para>
    /// Nothing here mutates process state. .NET carries UTF-8, UTF-16, UTF-32 and Latin-1 in the box; the
    /// wider set of legacy code pages needs <c>CodePagesEncodingProvider</c>, and registering that is the
    /// application's decision to make once at start-up rather than a library's to make behind its back.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Where a secondary result document goes: bytes if the caller supplied a stream resolver, text if it
    /// supplied the older one.
    /// </summary>
    /// <remarks>
    /// Two shapes rather than one because they are not interchangeable. Only the stream carries the
    /// document's declared encoding through to the bytes; the writer can be handed characters and no more.
    /// </remarks>
    internal readonly struct ResultDestination
    {
        /// <summary>Initializes a destination that takes characters.</summary>
        /// <param name="writer">The writer the caller's resolver supplied.</param>
        public ResultDestination(TextWriter writer)
        {
            Writer = writer;
            Stream = null;
        }

        /// <summary>Initializes a destination that takes bytes.</summary>
        /// <param name="stream">The stream the caller's resolver supplied.</param>
        public ResultDestination(Stream stream)
        {
            Writer = null;
            Stream = stream;
        }

        /// <summary>The writer to use, or <see langword="null"/> when this destination takes bytes.</summary>
        public TextWriter? Writer { get; }

        /// <summary>The stream to use, or <see langword="null"/> when this destination takes characters.</summary>
        public Stream? Stream { get; }
    }

    internal static class SerializationEncoding
    {
        /// <summary>
        /// Creates a writer that puts characters into a stream as the settings say to.
        /// </summary>
        /// <remarks>
        /// The settings handed back differ from those given in one respect: a byte-order mark that was asked
        /// for is cleared, because the encoding's preamble writes it and leaving it set would have the
        /// serializer write U+FEFF as a character on top of the bytes already there.
        /// </remarks>
        /// <param name="stream">The stream to write to. Left open when the writer is disposed.</param>
        /// <param name="settings">The settings declared for this result.</param>
        /// <param name="serializeWith">The settings to serialize with.</param>
        /// <returns>The writer, which the caller disposes to flush it.</returns>
        /// <exception cref="XsltException">The declared encoding is not one this process has.</exception>
        public static StreamWriter CreateWriter(
            Stream stream,
            OutputSettings settings,
            out OutputSettings serializeWith)
        {
            Encoding encoding = Resolve(settings);
            serializeWith = settings;

            if (settings.ByteOrderMark)
            {
                serializeWith = settings.Copy();
                serializeWith.ByteOrderMark = false;
            }

            return new StreamWriter(stream, encoding, leaveOpen: true);
        }

        /// <summary>
        /// Resolves the encoding named by <c>xsl:output</c>, configured to emit a byte-order mark or not.
        /// </summary>
        /// <param name="settings">The output settings, whose encoding and byte-order-mark are read.</param>
        /// <returns>The encoding to write the result in.</returns>
        /// <exception cref="XsltException">The name is not an encoding available to this process.</exception>
        public static Encoding Resolve(OutputSettings settings)
        {
            string name = settings.Encoding;

            // Constructed rather than looked up, because the shared instances carry a preamble decision that
            // cannot be changed afterwards and byte-order-mark is exactly that decision.
            if (Is(name, "UTF-8") || Is(name, "UTF8"))
            {
                return WithCharacterReferences(new UTF8Encoding(settings.ByteOrderMark));
            }

            // XML 1.0 §4.3.3 requires a byte-order mark on a UTF-16 document that carries no external
            // encoding declaration, so this one is not the stylesheet's to switch off.
            if (Is(name, "UTF-16") || Is(name, "UTF-16LE") || Is(name, "UTF16"))
            {
                return WithCharacterReferences(new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
            }

            if (Is(name, "UTF-16BE"))
            {
                return WithCharacterReferences(new UnicodeEncoding(bigEndian: true, byteOrderMark: true));
            }

            try
            {
                return WithCharacterReferences(Encoding.GetEncoding(name));
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.SESU0007,
                    $"This stylesheet asks for output in '{name}', which is not an encoding this process "
                    + "has. UTF-8, UTF-16, UTF-32 and ISO-8859-1 are always available; the legacy code pages "
                    + "need System.Text.Encoding.CodePages, registered by the application with "
                    + "Encoding.RegisterProvider(CodePagesEncodingProvider.Instance).",
                    error);
            }
        }

        private static bool Is(string name, string candidate)
        {
            return string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the encoding with an encoder fallback that writes a character reference.
        /// </summary>
        /// <remarks>
        /// A character the encoding cannot represent is written as <c>&amp;#xNN;</c>, which is what XSLT asks
        /// for and what keeps the character recoverable. .NET's default would substitute <c>?</c>, losing it
        /// silently.
        /// <para>
        /// The backstop rather than the mechanism. <see cref="OutputWriter"/> writes the reference itself,
        /// because it is the only place that knows where a reference means anything — inside a CDATA section
        /// it does not, and the section has to stop for the character instead — and because it works when
        /// the caller supplies a <see cref="TextWriter"/> and no encoder runs at all. What reaches here is
        /// what the writer deliberately did not escape, chiefly text that <c>disable-output-escaping</c>
        /// asked to be written as it stands, and losing that silently would still be worse than saying it in
        /// a reference.
        /// </para>
        /// </remarks>
        private static Encoding WithCharacterReferences(Encoding encoding)
        {
            // Cloned because the instances the registry hands out are read-only and shared, and because a
            // clone keeps the preamble decision made above — going back through the registry would not.
            Encoding writable = (Encoding)encoding.Clone();
            writable.EncoderFallback = new CharacterReferenceFallback();

            return writable;
        }

        /// <summary>
        /// Writes a character the output encoding cannot represent as an XML character reference.
        /// </summary>
        private sealed class CharacterReferenceFallback : EncoderFallback
        {
            /// <inheritdoc/>
            public override int MaxCharCount => 12;

            /// <inheritdoc/>
            public override EncoderFallbackBuffer CreateFallbackBuffer()
            {
                return new Buffer();
            }

            private sealed class Buffer : EncoderFallbackBuffer
            {
                private string m_reference = string.Empty;
                private int m_index;

                public override int Remaining => m_reference.Length - m_index;

                public override bool Fallback(char unmapped, int index)
                {
                    m_reference = $"&#x{(int)unmapped:X};";
                    m_index = 0;
                    return true;
                }

                public override bool Fallback(char high, char low, int index)
                {
                    m_reference = $"&#x{char.ConvertToUtf32(high, low):X};";
                    m_index = 0;
                    return true;
                }

                public override char GetNextChar()
                {
                    return m_index < m_reference.Length ? m_reference[m_index++] : '\0';
                }

                public override bool MovePrevious()
                {
                    if (m_index == 0)
                    {
                        return false;
                    }

                    m_index--;
                    return true;
                }

                public override void Reset()
                {
                    m_reference = string.Empty;
                    m_index = 0;
                }
            }
        }
    }
}
