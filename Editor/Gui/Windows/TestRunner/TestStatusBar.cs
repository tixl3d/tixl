#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.TestRunner;

/// <summary>
/// Draws a compact per-step status bar (one colored tick per step) for a set's last run, and reports
/// which tick the mouse is over so the caller can show that step's outcome and comment on hover.
/// </summary>
internal static class TestStatusBar
{
    internal static Color OutcomeColor(Outcome outcome) =>
        outcome switch
            {
                Outcome.Pass    => UiColors.StatusOkay,
                Outcome.Fail    => UiColors.StatusWarning,
                Outcome.Other   => UiColors.StatusAttention,
                Outcome.Skipped => UiColors.TextMuted,
                _               => UiColors.TextDisabled, // Pending
            };

    /// <summary>
    /// Draws the ticks left-aligned at <paramref name="leftX"/> with the whole bar (1px padded, rounded
    /// background frame included) bottoming out at <paramref name="frameBottomY"/>. Returns the hovered
    /// step index, or -1.
    /// </summary>
    internal static int Draw(ImDrawListPtr dl, float leftX, float frameBottomY, IReadOnlyList<TestRunResults.StepInfo> steps, float scale)
    {
        if (steps.Count == 0)
            return -1;

        var tickW = 4 * scale;
        var tickH = 2 * scale;
        var gap = 1 * scale;
        var pad = 1 * scale;      // background frame padding around the ticks
        var hitPadY = 2 * scale;  // thin ticks are hard to hover, so grow the hit area vertically

        var tickBottom = frameBottomY - pad;
        var tickTop = tickBottom - tickH;
        var barWidth = steps.Count * tickW + (steps.Count - 1) * gap;

        // 25% background frame behind the ticks (the only rounded part — ticks themselves are square).
        dl.AddRectFilled(new Vector2(leftX - pad, tickTop - pad),
                         new Vector2(leftX + barWidth + pad, frameBottomY),
                         ImGui.GetColorU32(UiColors.BackgroundFull.Fade(0.25f).Rgba),
                         2 * scale);

        var mouse = ImGui.GetMousePos();
        var hovered = -1;
        var x = leftX;

        for (var i = 0; i < steps.Count; i++)
        {
            // Ticks with a comment stay full opacity so the ones the tester annotated stand out.
            var color = OutcomeColor(steps[i].Outcome);
            if (string.IsNullOrWhiteSpace(steps[i].Comment))
                color = color.Fade(0.5f);

            dl.AddRectFilled(new Vector2(x, tickTop), new Vector2(x + tickW, tickBottom), ImGui.GetColorU32(color.Rgba));

            if (mouse.X >= x && mouse.X <= x + tickW && mouse.Y >= tickTop - hitPadY && mouse.Y <= tickBottom + hitPadY)
                hovered = i;

            x += tickW + gap;
        }

        return hovered;
    }
}
