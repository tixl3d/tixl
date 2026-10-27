#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Draws suggestion and key-hint text in three styles: keys as outlined keycap chips, text to type brightly, and
/// the description of what they do faintly. Markup: "[Space] presets" for keys, "{10}" for text to type.
/// Parse on change, draw per frame without allocating.
/// </summary>
internal static class KeyHintText
{
    internal enum Kinds
    {
        Description,
        Key,
        Typed,
    }

    internal readonly record struct Segment(string Text, Kinds Kind);

    internal static void Parse(string markup, List<Segment> segments)
    {
        segments.Clear();
        var start = 0;
        var kind = Kinds.Description;
        for (var index = 0; index <= markup.Length; index++)
        {
            var isEnd = index == markup.Length;
            if (!isEnd && markup[index] is not ('[' or ']' or '{' or '}'))
                continue;

            if (index > start)
                segments.Add(new Segment(markup[start..index], kind));

            if (!isEnd)
            {
                kind = markup[index] switch
                           {
                               '[' => Kinds.Key,
                               '{' => Kinds.Typed,
                               _   => Kinds.Description,
                           };
            }

            start = index + 1;
        }
    }

    /// <param name="position">Top left of the first line of text</param>
    /// <param name="maxWidth">Wraps before a key that wouldn't fit together with its description</param>
    internal static void Draw(ImDrawListPtr drawList, Vector2 position, List<Segment> segments, float maxWidth = float.PositiveInfinity)
    {
        var scale = T3Ui.UiScaleFactor;
        var chipPadding = MathF.Round(ChipPaddingPx * scale);
        var chipRounding = ChipRoundingPx * scale;
        var brightColor = UiColors.Text.Fade(BrightOpacity);
        var chipColor = UiColors.Text.Fade(ChipOutlineOpacity);
        var descriptionColor = UiColors.Text.Fade(DescriptionOpacity);
        var lineHeight = ImGui.GetTextLineHeight();
        var lineStartX = position.X;

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var textWidth = ImGui.CalcTextSize(segment.Text).X;
            if (StartsNewLine(segments, index, position.X - lineStartX, maxWidth, chipPadding))
            {
                position.X = lineStartX;
                position.Y += lineHeight + LineGapPx * scale;
            }

            if (segment.Kind != Kinds.Key)
            {
                drawList.AddText(position, segment.Kind == Kinds.Typed ? brightColor : descriptionColor, segment.Text);
                position.X += textWidth;
                continue;
            }

            // Keycap: outline on whole pixels around the text, spaced away from its neighbours
            position.X += chipPadding;
            var min = new Vector2(MathF.Floor(position.X - chipPadding), MathF.Floor(position.Y));
            var max = new Vector2(MathF.Floor(position.X + textWidth + chipPadding), MathF.Floor(position.Y + lineHeight));
            drawList.AddRect(min, max, chipColor, chipRounding);
            drawList.AddText(position, brightColor, segment.Text);
            position.X += textWidth + chipPadding * 2;
        }
    }

    /** Number of lines the segments take when wrapped at <paramref name="maxWidth"/> */
    internal static int CountLines(List<Segment> segments, float maxWidth)
    {
        var chipPadding = MathF.Round(ChipPaddingPx * T3Ui.UiScaleFactor);
        var lines = segments.Count > 0 ? 1 : 0;
        var x = 0f;
        for (var index = 0; index < segments.Count; index++)
        {
            if (StartsNewLine(segments, index, x, maxWidth, chipPadding))
            {
                lines++;
                x = 0;
            }

            var width = ImGui.CalcTextSize(segments[index].Text).X;
            x += segments[index].Kind == Kinds.Key ? width + chipPadding * 3 : width;
        }

        return lines;
    }

    internal static float GetLineHeight() => ImGui.GetTextLineHeight() + LineGapPx * T3Ui.UiScaleFactor;

    /** A key starts a new line when it and the description after it don't fit anymore */
    private static bool StartsNewLine(List<Segment> segments, int index, float lineWidth, float maxWidth, float chipPadding)
    {
        if (lineWidth <= 0 || segments[index].Kind != Kinds.Key)
            return false;

        var needed = ImGui.CalcTextSize(segments[index].Text).X + chipPadding * 3;
        if (index + 1 < segments.Count && segments[index + 1].Kind == Kinds.Description)
            needed += ImGui.CalcTextSize(segments[index + 1].Text.AsSpan().TrimEnd()).X;

        return lineWidth + needed > maxWidth;
    }

    private const float LineGapPx = 4;
    private const float BrightOpacity = 0.6f;
    private const float ChipOutlineOpacity = 0.3f;
    private const float DescriptionOpacity = 0.3f;
    private const float ChipPaddingPx = 3;
    private const float ChipRoundingPx = 3;
}
