#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.MagGraph.Interaction;

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
    internal static void Draw(ImDrawListPtr drawList, Vector2 position, List<Segment> segments)
    {
        var scale = T3Ui.UiScaleFactor;
        var chipPadding = MathF.Round(ChipPaddingPx * scale);
        var chipRounding = ChipRoundingPx * scale;
        var brightColor = UiColors.Text.Fade(BrightOpacity);
        var chipColor = UiColors.Text.Fade(ChipOutlineOpacity);
        var descriptionColor = UiColors.Text.Fade(DescriptionOpacity);
        var lineHeight = ImGui.GetTextLineHeight();

        foreach (var segment in segments)
        {
            var textWidth = ImGui.CalcTextSize(segment.Text).X;
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

    private const float BrightOpacity = 0.6f;
    private const float ChipOutlineOpacity = 0.3f;
    private const float DescriptionOpacity = 0.3f;
    private const float ChipPaddingPx = 3;
    private const float ChipRoundingPx = 3;
}
