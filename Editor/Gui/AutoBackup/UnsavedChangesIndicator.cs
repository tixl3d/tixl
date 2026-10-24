#nullable enable
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.AutoBackup;

/// <summary>
/// A quiet dot in the app bar while operators have unsaved changes. The tooltip lists them and when the
/// next automatic save happens; clicking saves right away.
/// </summary>
internal static class UnsavedChangesIndicator
{
    internal static void Draw()
    {
        RefreshModifiedSymbols();
        if (_modifiedSymbolNames.Count == 0)
            return;

        var scale = T3Ui.UiScaleFactor;
        var height = ImGui.GetFrameHeight();
        var width = MathF.Round(DotRadius * 2 * scale + 4 * scale);

        ImGui.SameLine(0, AppMenuBar.AppBarSpacingX);
        var pos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##unsavedChanges", new Vector2(width, height)) && !T3Ui.IsCurrentlySaving)
        {
            T3Ui.SaveInBackground(saveAll: false);
        }

        var isHovered = ImGui.IsItemHovered();
        var color = isHovered ? UiColors.ForegroundFull : UiColors.Text;
        ImGui.GetWindowDrawList().AddCircleFilled(pos + new Vector2(width, height) * 0.5f, DotRadius * scale, color, 12);

        if (!isHovered)
            return;

        CustomComponents.BeginTooltip(TooltipWidth);
        DrawTooltipContent();
        CustomComponents.EndTooltip();
    }

    private static void DrawTooltipContent()
    {
        var count = _modifiedSymbolNames.Count;
        CustomComponents.StylizedText(count == 1 ? "1 operator with unsaved changes" : $"{count} operators with unsaved changes",
                                      Fonts.FontNormal, UiColors.Text);
        FormInputs.AddVerticalSpace(2);

        var shownCount = Math.Min(count, MaxListedSymbols);
        for (var index = 0; index < shownCount; index++)
        {
            CustomComponents.StylizedText(_modifiedSymbolNames[index], Fonts.FontSmall, UiColors.TextMuted);
        }

        if (count > shownCount)
            CustomComponents.StylizedText($"and {count - shownCount} more", Fonts.FontSmall, UiColors.TextMuted);

        FormInputs.AddVerticalSpace(4);
        if (UserSettings.Config.EnableAutoBackup)
        {
            var minutesUntilBackup = (int)MathF.Ceiling((float)AutoBackup.SecondsUntilNextSave / 60);
            var nextBackup = minutesUntilBackup switch
                                 {
                                     <= 1 => "in less than a minute",
                                     _    => $"in {minutesUntilBackup} minutes",
                                 };
            var lastBackup = AutoBackup.SecondsSinceLastBackup is { } seconds
                                 ? $"Last backup {StringUtils.GetReadableRelativeTime(seconds)}."
                                 : "No backup in this session yet.";
            CustomComponents.HelpText($"Backup will run {nextBackup}. {lastBackup}");
        }
        else
        {
            CustomComponents.HelpText("Automatic backup is turned off in the settings.");
        }

        var shortcut = UserActions.Save.ListShortcuts();
        CustomComponents.HelpText(string.IsNullOrEmpty(shortcut) ? "Click to save now." : $"Click or press {shortcut} to save now.");
    }

    /** Polled, because enumerating every project's operators allocates and changes are rare. */
    private static void RefreshModifiedSymbols()
    {
        var time = ImGui.GetTime();
        if (time - _lastRefreshTime < RefreshIntervalSec)
            return;

        _lastRefreshTime = time;
        _modifiedSymbolNames.Clear();
        foreach (var project in EditableSymbolProject.AllProjects)
        {
            foreach (var symbolUi in project.SymbolUis.Values)
            {
                if (symbolUi.NeedsSaving)
                    _modifiedSymbolNames.Add(symbolUi.Symbol.Name);
            }
        }
    }

    private const float DotRadius = 3.5f;
    private const float TooltipWidth = 320;
    private const int MaxListedSymbols = 8;
    private const double RefreshIntervalSec = 0.5;

    private static readonly List<string> _modifiedSymbolNames = [];
    private static double _lastRefreshTime = double.NegativeInfinity;
}
