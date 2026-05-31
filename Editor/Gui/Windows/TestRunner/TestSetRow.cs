#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.TestRunner;

/// <summary>
/// Shared renderer for a guided-test-set row, used by the runner's pick list and the welcome window.
/// Draws the background, completion checkmark, title, scope, step count, warning icon, tag pills, and
/// the per-step status bar (with a per-tick hover tooltip). Selection and any extra tooltip (e.g. the
/// runner's intro markdown) are left to the caller, via the returned <see cref="Result"/>.
/// </summary>
internal static class TestSetRow
{
    /// <param name="Clicked">The row was clicked this frame.</param>
    /// <param name="Hovered">The row is hovered.</param>
    /// <param name="TickHovered">A status tick is hovered (its tooltip is already shown — suppress others).</param>
    internal readonly record struct Result(bool Clicked, bool Hovered, bool TickHovered);

    internal static Result Draw(TestSet set, bool selected, float height)
    {
        var scale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();
        ImGui.PushID(set.Id);

        var rowSize = new Vector2(ImGui.GetContentRegionAvail().X, height * scale);
        var clicked = ImGui.InvisibleButton("##row", rowSize);
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        var bg = selected
                     ? Color.Mix(UiColors.StatusActivated, UiColors.BackgroundFull, 0.2f).Fade(hovered ? 1f : 0.9f)
                     : hovered
                         ? UiColors.ForegroundFull.Fade(0.1f)
                         : UiColors.ForegroundFull.Fade(0.04f);
        dl.AddRectFilled(min, max, bg, 4 * scale);

        var textColor = selected ? UiColors.ForegroundFull : UiColors.Text;
        var muted = selected ? UiColors.ForegroundFull.Fade(0.75f) : UiColors.TextMuted;
        var padX = 12 * scale;
        var padY = 6 * scale;

        // Last-run result drives the left-gutter checkmark and the per-step status bar.
        TestRunResults.SetResult? runResult = null;
        if (TestRunResults.TryGet(set.Id, out var found))
            runResult = found;

        var statusGutter = 18 * scale; // always reserved so titles align whether or not a checkmark is shown
        if (runResult != null)
        {
            var checkColor = runResult.HadIssues ? UiColors.StatusAttention : UiColors.StatusOkay;
            Icons.DrawIconAtScreenPosition(Icon.Checkmark, new Vector2(min.X + padX, min.Y + padY), dl, checkColor);
        }

        var leftX = min.X + padX + statusGutter;

        // Title (top-left).
        dl.AddText(Fonts.FontBold, Fonts.FontBold.FontSize, new Vector2(leftX, min.Y + padY), textColor, set.Title);

        // Scope (bottom-left).
        if (!string.IsNullOrEmpty(set.Scope))
            dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize,
                       new Vector2(leftX, min.Y + padY + Fonts.FontBold.FontSize + 2 * scale), muted, set.Scope);

        // Step count (top-right).
        var stepCount = $"{set.Steps.Count} step{(set.Steps.Count == 1 ? "" : "s")}";
        ImGui.PushFont(Fonts.FontSmall);
        var stepsWidth = ImGui.CalcTextSize(stepCount).X;
        ImGui.PopFont();
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(max.X - padX - stepsWidth, min.Y + padY), muted, stepCount);

        // Warning icon (parse warnings) next to the step count.
        if (set.ParseWarnings.Count > 0)
        {
            var warnX = max.X - padX - stepsWidth - 18 * scale;
            Icons.DrawIconAtScreenPosition(Icon.Warning, new Vector2(warnX, min.Y + padY - 1 * scale), dl, UiColors.StatusWarning);
        }

        // Tag pills (bottom-right).
        var rightCursor = max.X - padX;
        DrawTagPills(dl, set.Tags, ref rightCursor, max.Y - padY - Fonts.FontSmall.FontSize, muted, scale);

        // Per-step status bar (bottom-left, 3px from the row bottom) + per-tick hover tooltip.
        var tickHovered = false;
        if (runResult != null)
        {
            var hoveredTick = TestStatusBar.Draw(dl, leftX, max.Y - 3 * scale, runResult.Steps, scale);
            if (hovered && hoveredTick >= 0)
            {
                tickHovered = true;
                var stepTitle = hoveredTick < set.Steps.Count ? set.Steps[hoveredTick].Title : $"Step {hoveredTick + 1}";
                DrawStepTooltip(stepTitle, runResult.Steps[hoveredTick], scale);
            }
        }

        ImGui.PopID();
        return new Result(clicked, hovered, tickHovered);
    }

    private static void DrawStepTooltip(string stepTitle, TestRunResults.StepInfo info, float scale)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * scale);
        ImGui.BeginTooltip();

        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted(stepTitle);
        ImGui.PopFont();

        ImGui.TextColored(TestStatusBar.OutcomeColor(info.Outcome), info.Outcome.ToString());

        if (!string.IsNullOrWhiteSpace(info.Comment))
        {
            ImGui.PushTextWrapPos(320 * scale);
            ImGui.TextWrapped(info.Comment);
            ImGui.PopTextWrapPos();
        }

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    private static void DrawTagPills(ImDrawListPtr dl, IReadOnlyList<string> tags, ref float rightX, float y, Color textColor, float scale)
    {
        if (tags.Count == 0)
            return;

        ImGui.PushFont(Fonts.FontSmall);
        var pillPadX = 6 * scale;
        var pillPadY = 1 * scale;
        var fontSize = Fonts.FontSmall.FontSize;
        var bgColor = ImGui.GetColorU32(UiColors.ForegroundFull.Fade(0.12f).Rgba);
        var fgColor = ImGui.GetColorU32(textColor.Rgba);

        for (var i = tags.Count - 1; i >= 0; i--)
        {
            var label = tags[i].ToUpperInvariant();
            var w = ImGui.CalcTextSize(label).X + pillPadX * 2;
            rightX -= w;
            dl.AddRectFilled(new Vector2(rightX, y - pillPadY), new Vector2(rightX + w, y + fontSize + pillPadY), bgColor, 6 * scale);
            dl.AddText(Fonts.FontSmall, fontSize, new Vector2(rightX + pillPadX, y), fgColor, label);
            rightX -= 4 * scale;
        }

        ImGui.PopFont();
    }
}
