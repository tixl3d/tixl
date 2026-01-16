#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;

namespace T3.Editor.Gui.UiHelpers;

internal static class CustomImguiDraw
{
    private static readonly int[] _wrapLineIndices = new int[10];

    // The method now accepts a Span of ReadOnlySpans for the wrapped lines to avoid allocations
    public static void AddWrappedCenteredText(ImDrawListPtr dl,
                                              string text,
                                              Vector2 position,
                                              int wrapCharCount,
                                              Color color,
                                              float verticalAlign = 0.5f)
    {
        var len = text.Length;
        var current = 0;
        var count = 0;

        while (current < len && count < _wrapLineIndices.Length)
        {
            var lineEnd = current + wrapCharCount;
            if (lineEnd >= len)
            {
                _wrapLineIndices[count++] = current;
                break;
            }

            var wrapPoint = lineEnd;

            // search for a valid line break
            var foundBreak = false;
            for (var i = lineEnd; i > current; i--)
            {
                var c = text[i - 1]; // ← look at the preceding character

                if (IsBreakChar(c))
                {
                    wrapPoint = i; // break AFTER this character
                    foundBreak = true;
                    break;
                }
            }

            // if no reasonable break found → hard wrap
            if (!foundBreak)
                wrapPoint = lineEnd;

            _wrapLineIndices[count++] = current;
            current = wrapPoint;
        }

        var lineHeight = ImGui.GetTextLineHeight();
        var totalHeight = lineHeight * count;
        var yStart = position.Y - totalHeight * verticalAlign;

        for (var i = 0; i < count; i++)
        {
            var start = _wrapLineIndices[i];
            var end = (i + 1 < count) ? _wrapLineIndices[i + 1] : len;

            var line = text.AsSpan(start, end - start);

            var w = ImGui.CalcTextSize(line).X;
            var xStart = position.X - w * 0.5f;

            dl.AddText(new Vector2(xStart, yStart + i * lineHeight), color, line);
        }

        return;

        // ---------------------------------------------------------
        // local helper
        // ---------------------------------------------------------
        static bool IsBreakChar(char c)
        {
            // break AFTER whitespace or punctuation
            return char.IsWhiteSpace(c)
                   || c == '-'
                   || c == ','
                   || c == '.'
                   || c == ';'
                   || c == ':'
                   || c == '!'
                   || c == '?';
        }
    }

    private static readonly Vector2[] _pointsForNgon = new Vector2[MaxNgonCorners];
    private const int MaxNgonCorners = 8;

    public static void AddNgonRotated(this ImDrawListPtr dl, Vector2 center, float radius, uint color, bool filled = true, int count = 6,
                                      float startAngle = -MathF.PI / 2f)
    {
        count = count.ClampMax(MaxNgonCorners);

        for (var i = 0; i < count; i++)
        {
            var a = startAngle + i * (2 * MathF.PI / count);
            _pointsForNgon[i] = new Vector2(
                                            center.X + MathF.Cos(a) * radius,
                                            center.Y + MathF.Sin(a) * radius
                                           );
        }

        if (filled)
        {
            dl.AddConvexPolyFilled(ref _pointsForNgon[0], count, color);
        }
        else
        {
            dl.AddPolyline(ref _pointsForNgon[0], count, color, ImDrawFlags.Closed, 2);
        }
    }
}