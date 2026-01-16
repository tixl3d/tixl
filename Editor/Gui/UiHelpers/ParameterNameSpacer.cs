#nullable enable
namespace T3.Editor.Gui.UiHelpers;

public static class ParameterNameSpacer
{
    /// <summary>
    /// Prepares inserts spaces to Pascal-case parameter strings. 
    /// </summary>
    /// <remarks>
    /// It will try to avoid allocations, but it is NOT thread-safe and the result is meant for immediate output with imgui-methods like Text(). 
    /// </remarks>
    public static ReadOnlySpan<char> AddSpacesForImGuiOutput(this string s)
    {
        if (!UserSettings.Config.AddSpacesToParameterNames)
            return s.AsSpan();

        if (string.IsNullOrEmpty(s))
        {
            return ReadOnlySpan<char>.Empty;
        }

        _processingBuffer ??= new char[MaxOutputLength];

        var writeIdx = 0;

        // Handle first character
        _processingBuffer[writeIdx++] = s[0];

        for (var readIdx = 1; readIdx < s.Length && writeIdx < MaxOutputLength - 1; readIdx++)
        {
            var currentChar = s[readIdx];
            var previousChar = s[readIdx - 1];

            // Check if next character exists for lookahead
            var hasNext = readIdx + 1 < s.Length;
            var nextChar = hasNext ? s[readIdx + 1] : '\0';

            // Add space only for these specific transitions:
            // - lowercase → uppercase (e.g., "Box" + "SDF" = "Box SDF")
            // - letter → digit (e.g., "Layer" + "2" = "Layer 2")
            // - digit → letter, BUT NOT if current letter is uppercase and next is also uppercase or digit
            //   (to preserve acronyms like "2D", "3D")
            var needsSpace =
                (char.IsLower(previousChar) && char.IsUpper(currentChar)) ||
                (char.IsLetter(previousChar) && char.IsDigit(currentChar)) ||
                (char.IsDigit(previousChar) && char.IsLetter(currentChar) &&
                !(char.IsUpper(currentChar) && (!hasNext || char.IsUpper(nextChar))));

            if (needsSpace && writeIdx < MaxOutputLength - 1)
            {
                _processingBuffer[writeIdx++] = ' ';
            }

            _processingBuffer[writeIdx++] = currentChar;
        }

        // Return a span covering the written part of the reusable buffer.
        // The contents of this span will be overwritten by the next call to this method
        // on the same thread.
        return new ReadOnlySpan<char>(_processingBuffer, 0, writeIdx);
    }

    [ThreadStatic]
    private static char[]? _processingBuffer;

    private const int MaxOutputLength = 256;
}