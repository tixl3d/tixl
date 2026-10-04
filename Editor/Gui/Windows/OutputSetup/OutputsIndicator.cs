#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The app bar's outputs chip: how many outputs the active setup is presenting right now, and — on hover —
/// which ones, where they go and what each costs the editor's frame. Presenting runs whether or not any output
/// UI is open, so without this the only symptom of an expensive setup is a slow editor.
/// </summary>
internal static class OutputsIndicator
{
    /// <summary>Draws nothing while nothing is presented: no chip is the answer to "is anything rendering?".</summary>
    public static void Draw()
    {
        var entries = OutputPresentationStats.Entries;
        if (entries.Count == 0)
            return;

        var scale = T3Ui.UiScaleFactor;

        // The chip carries the cost: presenting a couple of outputs is unremarkable, but once it eats most of a
        // frame it should catch the eye without anyone having to open the Performance window.
        var load = MathUtils.RemapAndClamp(OutputPresentationStats.TotalMs, UnremarkableMs, HeavyMs, 0, 1);
        var color = Color.Mix(UiColors.ForegroundFull.Fade(0.5f), UiColors.StatusAttention.Fade(0.9f), load);

        // A single output needs no count — the chip itself says there is one.
        var countLabel = entries.Count > 1 ? $"{entries.Count}×" : string.Empty;
        ImGui.PushFont(Fonts.FontNormal);
        var countSize = countLabel.Length == 0 ? Vector2.Zero : ImGui.CalcTextSize(countLabel);
        ImGui.PopFont();

        Icons.GetGlyphDefinition(Icon.HoverPreviewPlay, out _, out var iconSize);
        var gap = countLabel.Length == 0 ? 0 : 4 * scale;
        var height = ImGui.GetFrameHeight();

        ImGui.SameLine(0, AppMenuBar.AppBarSpacingX);
        var chipPos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##outputsChip", new Vector2(iconSize.X + gap + countSize.X, height)))
            Output.OutputWindow.EnterSetupOnPrimaryWindow();

        // Both centred on the bar's own height rather than on each other: the glyph and the digits have
        // different extents, and aligning them to the same middle is what makes them sit level.
        var dl = ImGui.GetWindowDrawList();
        Icons.DrawIconAtScreenPosition(Icon.HoverPreviewPlay,
                                       new Vector2(chipPos.X, MathF.Round(chipPos.Y + (height - iconSize.Y) * 0.5f)), dl, color);
        if (countLabel.Length > 0)
        {
            dl.AddText(Fonts.FontNormal, Fonts.FontNormal.FontSize,
                       new Vector2(MathF.Round(chipPos.X + iconSize.X + gap), MathF.Round(chipPos.Y + (height - countSize.Y) * 0.5f)),
                       color, countLabel);
        }

        if (!ImGui.IsItemHovered())
            return;

        CustomComponents.BeginTooltip(SummaryWidth * T3Ui.UiScaleFactor);
        DrawActiveOutputsSummary();
        CustomComponents.EndTooltip();
    }

    /// <summary>
    /// What is presenting right now, as tooltip rows — also drawn inside the Output Setup button's tooltip, so
    /// the answer is in both places the question comes up.
    /// </summary>
    public static void DrawActiveOutputsSummary()
    {
        var scale = T3Ui.UiScaleFactor;
        var width = SummaryWidth * scale;
        var entries = OutputPresentationStats.Entries;
        if (entries.Count == 0)
        {
            CustomComponents.StylizedText("No outputs presenting", Fonts.FontNormal, UiColors.TextMuted);
            return;
        }

        // The rows lay their own columns out to the right edge; the host's wrap position would break the
        // durations onto a second line.
        ImGui.PushTextWrapPos(float.MaxValue);
        OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig);

        // The longest bar is the slowest output, so the rows compare against each other rather than against
        // a frame budget the editor doesn't have.
        var maxMs = 0.001f;
        for (var i = 0; i < entries.Count; i++)
        {
            maxMs = MathF.Max(maxMs, entries[i].DurationMs);
        }

        // With one output the sum is that output's own row again, so the header is a label and nothing more.
        var total = entries.Count > 1 ? OutputPresentationStats.TotalMs : (float?)null;
        DrawRow($"{entries.Count} Active Outputs", UiColors.Text, null, total, maxMs, width, isTotal: true);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var output = setup?.FindOutput(entry.OutputId);
            var binding = machineConfig?.FindBinding(entry.OutputId);
            var detail = output == null || binding == null
                             ? null
                             : $"{output.ResolvedResolution.Width}×{output.ResolvedResolution.Height} → {Plugs.BindingLabel(machineConfig!, binding)}";

            DrawRow(entry.Name, SetupColors.LabelFor(SetupEntityKinds.Output), detail, entry.DurationMs, maxMs, width,
                    withBackdrop: true);
        }

        ImGui.PopTextWrapPos();
    }

    /// <summary>One row: name, where it goes, its share of the slowest output as a bar, and its duration.</summary>
    private static void DrawRow(string name, Color nameColor, string? detail, float? durationMs, float maxMs, float width,
                                bool isTotal = false, bool withBackdrop = false)
    {
        var scale = T3Ui.UiScaleFactor;
        var rowPos = SetupStrokes.Snap(ImGui.GetCursorScreenPos());
        var height = MathF.Round(ImGui.GetTextLineHeight() + RowPadding * scale);
        var barMaxX = rowPos.X + width - DurationWidth * scale;
        var barMinX = barMaxX - BarWidth * scale;

        if (withBackdrop)
        {
            ImGui.GetWindowDrawList()
                 .AddRectFilled(rowPos, new Vector2(rowPos.X + width, rowPos.Y + height), UiColors.BackgroundFull.Fade(0.1f));
        }

        var textY = MathF.Round(rowPos.Y + (height - ImGui.GetTextLineHeight()) * 0.5f);
        ImGui.SetCursorScreenPos(new Vector2(rowPos.X, textY));
        CustomComponents.StylizedText(name, Fonts.FontNormal, nameColor);

        if (detail != null)
        {
            ImGui.SameLine(0, 8 * scale);
            CustomComponents.StylizedText(detail, Fonts.FontSmall, UiColors.TextMuted.Fade(0.6f));
        }

        if (durationMs != null)
        {
            var dl = ImGui.GetWindowDrawList();
            var barHeight = MathF.Round(5 * scale);
            var barY = MathF.Round(rowPos.Y + (height - barHeight) * 0.5f);
            dl.AddRectFilled(new Vector2(MathF.Round(barMinX), barY), new Vector2(MathF.Round(barMaxX), barY + barHeight),
                             UiColors.BackgroundFull.Fade(0.5f));
            dl.AddRectFilled(new Vector2(MathF.Round(barMinX), barY),
                             new Vector2(MathF.Round(barMinX + (barMaxX - barMinX) * MathUtils.Clamp(durationMs.Value / maxMs, 0, 1)),
                                         barY + barHeight),
                             (isTotal ? UiColors.StatusAttention : UiColors.TextMuted).Fade(0.7f));

            // The total is what the frame actually pays, so it carries the attention hue; one output is just its share.
            var duration = $"{durationMs.Value:0.0}ms";
            ImGui.SetCursorScreenPos(new Vector2(rowPos.X + width - ImGui.CalcTextSize(duration).X, textY));
            CustomComponents.StylizedText(duration, Fonts.FontNormal, isTotal ? UiColors.StatusAttention : UiColors.Text);
        }

        // The rows are backdrops rather than lines, so they need a gap to read as separate.
        ImGui.SetCursorScreenPos(new Vector2(rowPos.X, rowPos.Y + height + 1 * scale));
        ImGui.Dummy(new Vector2(width, 0));
    }

    // Unscaled px: the summary's width, the columns the bar and the duration are right-aligned in, and the
    // breathing room around a row's text.
    // Milliseconds of combined presenting the chip fades between: unremarkable, and eating most of a frame.
    private const float UnremarkableMs = 5;
    private const float HeavyMs = 14;

    private const float SummaryWidth = 320;
    private const float BarWidth = 54;
    private const float DurationWidth = 58;
    private const float RowPadding = 6;
}
