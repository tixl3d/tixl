using System.Runtime.CompilerServices;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// Supports helper for allocation free string interpolation.
///
/// For rendering flexible string content with ImGui generating new string causes a lot of GC
/// allocations. This class provides some helpers to optimized the most common usecases.
/// </summary>
public static class SpanFormat
{
    
        
    /// <summary>
    /// Formats an interpolated string directly into a stack-allocated buffer to avoid heap allocations.
    /// </summary>
    /// <param name="buffer">The destination span, typically created via <see langword="stackalloc"/>.</param>
    /// <param name="handler">An internal handler that performs the zero-allocation formatting logic.</param>
    /// <returns>A view into the portion of the buffer containing the formatted characters.</returns>
    /// <remarks>
    /// This is intended for high-performance UI loops where generating temporary strings would cause GC pressure.
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;char&gt; buffer = stackalloc char[32];
    /// var label = buffer.Format($"{count} Items##{id}\0");
    /// ImGui.TextUnformatted(label);
    /// </code>
    /// </example>
    public static ReadOnlySpan<char> Format(this Span<char> buffer, 
                                            [InterpolatedStringHandlerArgument("buffer")] 
                                            TryWriteHandler handler)
    {
        return buffer[..handler.CharsWritten];
    }
    
    // This simple handler tells the compiler how to use TryWrite under the hood
    [InterpolatedStringHandler]
    public ref struct TryWriteHandler
    {
        private readonly Span<char> _destination;
        public int CharsWritten { get; private set; }

        public TryWriteHandler(int literalLength, int holeCount, Span<char> destination)
        {
            _destination = destination;
            CharsWritten = 0;
        }

        public bool AppendLiteral(string value)
        {
            if (value.AsSpan().TryCopyTo(_destination[CharsWritten..]))
            {
                CharsWritten += value.Length;
                return true;
            }
            return false;
        }

        public bool AppendFormatted<T>(T value)
        {
            // Optimized path for numbers and other formattable types
            if (value is ISpanFormattable formattable)
            {
                if (formattable.TryFormat(_destination[CharsWritten..], out int written, default, default))
                {
                    CharsWritten += written;
                    return true;
                }
            }
        
            // Fallback for strings or other types
            var s = value?.ToString();
            if (s != null && s.AsSpan().TryCopyTo(_destination[CharsWritten..]))
            {
                CharsWritten += s.Length;
                return true;
            }
            return false;
        }
    }
}