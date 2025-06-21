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

        var previousCharForLogic = s[0];

        for (var readIdx = 0; readIdx < s.Length && writeIdx < MaxOutputLength - 1; readIdx++)
        {
            var currentChar = s[readIdx];
            var needsSpace = false;

            // Determine if a space is needed based on character types or casing.
            if ((char.IsNumber(previousCharForLogic) && !char.IsNumber(currentChar)) ||
                (char.IsNumber(currentChar) && !char.IsNumber(previousCharForLogic)))
            {
                needsSpace = true;
            }
            else if (char.IsUpper(currentChar))
            {
                needsSpace = true;
            }

            if (needsSpace)
            {
                _processingBuffer[writeIdx++] = ' ';
            }

            _processingBuffer[writeIdx++] = currentChar;
            previousCharForLogic = currentChar;
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