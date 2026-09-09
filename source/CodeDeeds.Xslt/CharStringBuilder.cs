using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CodeDeeds.Xslt
{
    /// <summary>
    /// Builds a short string in a caller-supplied buffer, which is normally stack space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written as <c>new CharStringBuilder(stackalloc char[128])</c>. Where <see cref="System.Text.StringBuilder"/>
    /// costs an object, a chunk and the result, this costs only the result, so the string a transform actually
    /// wanted is the one allocation it makes.
    /// </para>
    /// <para>
    /// It is worth reaching for where the characters arrive one at a time and there is no bulk copy to use
    /// instead — <c>normalize-space</c> and <c>translate</c> inspect every character, so they have to. Where
    /// whole strings are appended, <see cref="string.Concat(string?[])"/> already makes one allocation and
    /// copies with the same machinery; this wins there only by not needing the array.
    /// </para>
    /// <para>
    /// It is a <see langword="ref"/> struct, so it cannot outlive the buffer it was handed. Pass it to a helper
    /// by <see langword="ref"/> and not by value: a copy has its own length, and appends to the copy are lost.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("Value = {m_buffer[..m_length]}, Length = {Length}")]
    internal ref struct CharStringBuilder
    {
        private Span<char> m_buffer;
        private int m_length;

        /// <summary>Initializes a builder over a buffer, which the caller normally stack-allocates.</summary>
        /// <param name="buffer">The space to build in. Growing past it moves to the heap.</param>
        public CharStringBuilder(Span<char> buffer)
        {
            m_buffer = buffer;
            m_length = 0;
        }

        /// <summary>Gets the number of characters written so far.</summary>
        public readonly int Length => m_length;

        /// <summary>Discards what has been written, keeping the buffer.</summary>
        public void Clear()
        {
            m_length = 0;
        }

        /// <summary>Appends one character.</summary>
        public void Append(char value)
        {
            if (m_length >= m_buffer.Length)
            {
                Grow(1);
            }

            m_buffer[m_length++] = value;
        }

        /// <summary>Appends one character several times, as padding does.</summary>
        /// <param name="value">The character to repeat.</param>
        /// <param name="count">How many times to write it. Zero writes nothing.</param>
        public void Append(char value, int count)
        {
            if (m_length + count > m_buffer.Length)
            {
                Grow(count);
            }

            m_buffer.Slice(m_length, count).Fill(value);
            m_length += count;
        }

        /// <summary>Appends a run of characters.</summary>
        public void Append(ReadOnlySpan<char> value)
        {
            if (m_length + value.Length > m_buffer.Length)
            {
                Grow(value.Length);
            }

            value.CopyTo(m_buffer[m_length..]);
            m_length += value.Length;
        }

        /// <summary>Appends a string, treating <see langword="null"/> as empty.</summary>
        public void Append(string? value)
        {
            if (value is not null)
            {
                Append(value.AsSpan());
            }
        }

        /// <summary>Appends a value in its invariant form, formatting straight into the buffer.</summary>
        /// <typeparam name="TValue">The type being formatted.</typeparam>
        /// <param name="value">The value to format.</param>
        /// <param name="format">The format string, or <see langword="null"/> for the type's default.</param>
        public void Append<TValue>(TValue value, string? format = null)
            where TValue : ISpanFormattable
        {
            int written;
            while (!value.TryFormat(m_buffer[m_length..], out written, format, CultureInfo.InvariantCulture))
            {
                Grow(1);
            }

            m_length += written;
        }

        /// <summary>Gets what has been written, without copying it.</summary>
        public readonly ReadOnlySpan<char> AsSpan() => m_buffer[..m_length];

        /// <summary>Copies what has been written into a new string.</summary>
        public readonly override string ToString() => new string(m_buffer[..m_length]);

        /// <summary>
        /// Moves to a heap buffer big enough for what is being appended.
        /// </summary>
        /// <remarks>
        /// Sized in one step rather than by repeated doubling, so an append far larger than the buffer is
        /// copied once. The floor matters: doubling a zero-length buffer never reaches the required size.
        /// </remarks>
        /// <param name="required">The number of characters about to be appended.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow(int required)
        {
            int capacity = Math.Max(Math.Max(m_buffer.Length * 2, m_length + required), 16);
            char[] grown = new char[capacity];
            m_buffer[..m_length].CopyTo(grown);
            m_buffer = grown;
        }
    }
}
