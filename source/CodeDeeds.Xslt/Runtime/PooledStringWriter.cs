using System.Buffers;
using System.Text;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// A text writer that gathers a result into a character buffer from the shared pool, to be handed over
    /// as one string at the end.
    /// </summary>
    /// <remarks>
    /// What <see cref="StringWriter"/> does with a <see cref="StringBuilder"/>, whose chunks are a new
    /// array every few thousand characters and are garbage the moment the string is made from them. A
    /// result is written once and read once, so the buffer that holds it on the way is borrowed and given
    /// back, and the only allocation a string-returning transformation makes for its output is the string.
    /// The buffer is sized from a guess the caller has — the input's length, usually — and doubles where
    /// the guess falls short; being pooled, a buffer that was once large is there for the next run.
    /// </remarks>
    internal sealed class PooledStringWriter : TextWriter
    {
        private char[] m_buffer;
        private int m_length;

        /// <summary>Initializes a writer with room for about as many characters as the caller expects.</summary>
        /// <param name="expectedLength">How long the result is likely to be; a guess, not a limit.</param>
        public PooledStringWriter(int expectedLength)
        {
            m_buffer = ArrayPool<char>.Shared.Rent(Math.Max(expectedLength, 1024));
        }

        /// <inheritdoc/>
        public override Encoding Encoding => Encoding.Unicode;

        /// <inheritdoc/>
        public override void Write(char value)
        {
            if (m_length == m_buffer.Length)
            {
                Grow(1);
            }

            m_buffer[m_length++] = value;
        }

        /// <inheritdoc/>
        public override void Write(string? value)
        {
            if (value is not null)
            {
                Write(value.AsSpan());
            }
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<char> buffer)
        {
            if (m_length + buffer.Length > m_buffer.Length)
            {
                Grow(buffer.Length);
            }

            buffer.CopyTo(m_buffer.AsSpan(m_length));
            m_length += buffer.Length;
        }

        /// <inheritdoc/>
        public override void Write(char[] buffer, int index, int count)
        {
            Write(buffer.AsSpan(index, count));
        }

        /// <inheritdoc/>
        public override void Write(char[]? buffer)
        {
            if (buffer is not null)
            {
                Write(buffer.AsSpan());
            }
        }

        /// <summary>Makes the string of what was written and gives the buffer back to the pool.</summary>
        public string Finish()
        {
            string result = new string(m_buffer, 0, m_length);
            Release();
            return result;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release();
            }

            base.Dispose(disposing);
        }

        private void Grow(int extra)
        {
            int needed = checked(m_length + extra);
            char[] bigger = ArrayPool<char>.Shared.Rent(Math.Max(needed, m_buffer.Length * 2));
            m_buffer.AsSpan(0, m_length).CopyTo(bigger);
            ArrayPool<char>.Shared.Return(m_buffer);
            m_buffer = bigger;
        }

        private void Release()
        {
            if (m_buffer.Length != 0)
            {
                ArrayPool<char>.Shared.Return(m_buffer);
                m_buffer = Array.Empty<char>();
                m_length = 0;
            }
        }
    }
}
