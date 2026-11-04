#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.TimeLine;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Window;

/// <summary>
/// Tool icons in the upper right corner of the graph window: the image-background tools while a
/// background is active, hover preview mode and the project settings.
/// </summary>
internal static class GraphToolCluster
{
    /** Vertical space the cluster covers at the top of the graph window, so other overlays can stay clear of it. */
    internal static float OccupiedHeight => UserSettings.Config.ShowToolbar
                                                ? IconSize.Y + 2 * Margin
                                                : 0;

    internal static void Draw(ProjectView projectView)
    {
        if (!UserSettings.Config.ShowToolbar)
            return;

        // Right-aligned with the width measured last frame: the image-background tools have a variable width.
        var width = MathF.Max(_lastContentWidth, IconSize.X);
        ImGui.SetCursorPos(new Vector2(ImGui.GetWindowWidth() - width - Margin, Margin));
        ImGui.BeginChild("##graphTools", new Vector2(width, IconSize.Y), ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.BeginGroup();
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 5) * T3Ui.UiScaleFactor);
            projectView.GraphImageBackground.DrawToolbarItems();
            ImGui.PopStyleVar();

            DrawHoverModeToggle();
            ImGui.SameLine();
            DrawProjectSettingsButton(projectView);
        }
        ImGui.EndGroup();
        _lastContentWidth = ImGui.GetItemRectSize().X;
        ImGui.EndChild();
    }

    private static void DrawHoverModeToggle()
    {
        Icon icon;
        string hoverModeTooltip;
        string? hoverModeAdditionalTooltip = null;
        var state = CustomComponents.ButtonStates.Emphasized;
        switch (UserSettings.Config.HoverMode)
        {
            case UserSettings.GraphHoverModes.Disabled:
                state = CustomComponents.ButtonStates.Default;
                icon = Icon.HoverPreviewDisabled;
                hoverModeTooltip = "No preview images on hover";
                break;
            case UserSettings.GraphHoverModes.Live:
                icon = Icon.HoverPreviewPlay;
                hoverModeTooltip = "Live Hover Preview - Render explicit thumbnail image.";
                hoverModeAdditionalTooltip = "This can interfere with the rendering of the current output.";
                break;
            default:
                icon = Icon.HoverPreviewSmall;
                hoverModeTooltip = "Last - Show the current state of the operator.";
                hoverModeAdditionalTooltip = "This can be outdated if operator is not require for current output.";
                break;
        }

        if (CustomComponents.IconButton(icon, IconSize, state))
        {
            UserSettings.Config.HoverMode =
                (UserSettings.GraphHoverModes)(((int)UserSettings.Config.HoverMode + 1) % Enum.GetNames(typeof(UserSettings.GraphHoverModes)).Length);
        }

        CustomComponents.TooltipForLastItem(hoverModeTooltip, hoverModeAdditionalTooltip);
    }

    private static void DrawProjectSettingsButton(ProjectView projectView)
    {
        var composition = projectView.CompositionInstance;
        var opHasSettings = composition != null
                            && PlaybackUtils.FindCompositionSettingsForInstance(composition, out var compositionWithSettings, out _)
                            && compositionWithSettings == composition;

        if (CustomComponents.IconButton(Icon.Settings, IconSize, opHasSettings
                                                                     ? CustomComponents.ButtonStates.Emphasized
                                                                     : CustomComponents.ButtonStates.Default))
        {
            WindowManager.ToggleInstanceVisibility<ProjectSettingsWindow>();
        }

        CustomComponents.TooltipForLastItem("Project Settings",
                                            "Switch between soundtrack and VJ modes. Control BPM and other inputs.");
    }

    private static Vector2 IconSize => new Vector2(TimeControls.ControlSize.Y);
    private static float Margin => 4 * T3Ui.UiScaleFactor;

    private static float _lastContentWidth;
}
