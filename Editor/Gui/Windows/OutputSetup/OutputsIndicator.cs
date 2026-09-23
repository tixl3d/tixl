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
        ImGui.SameLine(0, AppMenuBar.AppBarSpacingX);
        ImGui.BeginGroup();
        Icon.Projector.DrawAtCursor(UiColors.TextMuted);
        ImGui.SameLine(0, 4 * scale);
        CustomComponents.StylizedText(entries.Count.ToString(), Fonts.FontNormal, UiColors.TextMuted);
        ImGui.EndGroup();

        if (ImGui.IsItemClicked())
            Output.OutputWindow.EnterSetupOnPrimaryWindow();

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

        OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig);

        // The longest bar is the slowest output, so the rows compare against each other rather than against
        // a frame budget the editor doesn't have.
        var maxMs = 0.001f;
        for (var i = 0; i < entries.Count; i++)
        {
            maxMs = MathF.Max(maxMs, entries[i].DurationMs);
        }

        DrawRow($"{entries.Count} Active Outputs", UiColors.Text, null, OutputPresentationStats.TotalMs, maxMs, width);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var output = setup?.FindOutput(entry.OutputId);
            var binding = machineConfig?.FindBinding(entry.OutputId);
            var detail = output == null || binding == null
                             ? null
                             : $"{output.ResolvedResolution.Width}×{output.ResolvedResolution.Height} → {Plugs.BindingLabel(machineConfig!, binding)}";

            DrawRow(entry.Name, SetupColors.LabelFor(SetupEntityKinds.Output), detail, entry.DurationMs, maxMs, width);
        }
    }

    /// <summary>One row: name, where it goes, its share of the slowest output as a bar, and its duration.</summary>
    private static void DrawRow(string name, Color nameColor, string? detail, float durationMs, float maxMs, float width)
    {
        var scale = T3Ui.UiScaleFactor;
        var rowPos = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        var barMinX = rowPos.X + width - BarWidth * scale - DurationWidth * scale;
        var barMaxX = rowPos.X + width - DurationWidth * scale;

        ImGui.SetCursorScreenPos(rowPos);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(name, Fonts.FontNormal, nameColor);

        if (detail != null)
        {
            ImGui.SameLine(0, 8 * scale);
            CustomComponents.StylizedText(detail, Fonts.FontSmall, UiColors.TextMuted.Fade(0.7f));
        }

        var dl = ImGui.GetWindowDrawList();
        var barHeight = 6 * scale;
        var barY = rowPos.Y + (height - barHeight) * 0.5f;
        dl.AddRectFilled(new Vector2(barMinX, barY), new Vector2(barMaxX, barY + barHeight), UiColors.BackgroundFull.Fade(0.4f));
        dl.AddRectFilled(new Vector2(barMinX, barY),
                         new Vector2(barMinX + (barMaxX - barMinX) * MathUtils.Clamp(durationMs / maxMs, 0, 1), barY + barHeight),
                         UiColors.TextMuted.Fade(0.6f));

        var duration = $"{durationMs:0.0}ms";
        ImGui.SetCursorScreenPos(new Vector2(barMaxX + DurationWidth * scale - ImGui.CalcTextSize(duration).X, rowPos.Y));
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(duration, Fonts.FontNormal, UiColors.Text);

        ImGui.SetCursorScreenPos(new Vector2(rowPos.X, rowPos.Y + height));
        ImGui.Dummy(new Vector2(width, 0));
    }

    // Unscaled px: the summary's own width, and the columns the bar and the duration are right-aligned in.
    private const float SummaryWidth = 320;
    private const float BarWidth = 60;
    private const float DurationWidth = 50;
}
