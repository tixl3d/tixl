#nullable enable
using T3.Core.Animation;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.Layouts;

/// <summary>
/// Controls visibility of global ui elements like main menu etc.
/// </summary>
internal static class UiConfig
{
    internal static void ToggleFocusMode()
    {
        var projectView = ProjectView.Focused;
        if (projectView == null)
            return;

        if (!LayoutHandling.FocusMode)
        {
            TrySwitchToFocusMode(projectView);
        }
        else
        {
            SwitchBackFromFocusMode();
        }
    }

    private static bool TrySwitchToFocusMode(ProjectView projectView)
    {
        if (!OutputWindow.TryGetPrimaryOutputWindow(out var oldOutputWindow))
            return false;
        
        // We keep this to restore state when switching to a layout from focus mode.
        _uiStateBeforeFocusMode = KeepUiState(); 
        
        oldOutputWindow.Pinning.TryGetPinnedOrSelectedInstance(out var instance, out _);
        projectView.GraphImageBackground.OutputInstance = instance;

        HideAllUiElements();

        LayoutHandling.LoadAndApplyLayoutOrFocusMode(LayoutHandling.Layouts.FocusMode);
        
        LayoutHandling.FocusMode = true;
        return true;
    }

    /// <summary>
    /// Applying a non-focus layout triggers <see cref="RestoreBackgroundAfterFocusMode"/>, so the
    /// background cleanup is shared with direct layout switches (F1..F10) while in focus mode.
    /// </summary>
    private static void SwitchBackFromFocusMode()
    {
        var layoutIndex = _uiStateBeforeFocusMode?.WindowLayoutIndex ?? UserSettings.Config.WindowLayoutIndex;
        LayoutHandling.LoadAndApplyLayoutOrFocusMode((LayoutHandling.Layouts)layoutIndex);
    }

    /// <summary>
    /// Restores output pinning and clears the graph background when leaving focus mode.
    /// Called by <see cref="LayoutHandling"/> after the new layout has been applied,
    /// so the primary output window of that layout exists again.
    /// </summary>
    internal static void RestoreBackgroundAfterFocusMode()
    {
        LayoutHandling.FocusMode = false;

        var projectView = ProjectView.Focused;
        if (projectView == null)
            return;

        if (OutputWindow.TryGetPrimaryOutputWindow(out var newOutputWindow))
        {
            newOutputWindow.Pinning.PinInstance(projectView.GraphImageBackground.OutputInstance, projectView);
        }

        projectView.GraphImageBackground.ClearBackground();
    }


    internal static void ToggleAllUiElements()
    {
        if (UserSettings.Config.ShowToolbar)
        {
            HideAllUiElements();
        }
        else
        {
            ShowAllUiElements();
        }
    }

    internal static void RestoreUiVisibilityAfterFocusMode()
    {
        if (_uiStateBeforeFocusMode == null)
            return;

        ApplyUiVisibility(_uiStateBeforeFocusMode.ElementsVisibility);
        _uiStateBeforeFocusMode = null;
    }

    private static void ShowAllUiElements()
    {
        UserSettings.Config.ShowMainMenu = true;
        UserSettings.Config.ShowTitleAndDescription = true;
        UserSettings.Config.ShowToolbar = true;
        if (Playback.Current.Settings is { Playback.UsesBeatTapping: false })
        {
            UserSettings.Config.ShowTimeline = true;
        }
    }

    internal static void HideAllUiElements()
    {
        UserSettings.Config.ShowMainMenu = false;
        UserSettings.Config.ShowTitleAndDescription = false;
        UserSettings.Config.ShowToolbar = false;
        UserSettings.Config.ShowTimeline = false;
    }

    private static UiState? _uiStateBeforeFocusMode;

    internal static UiState KeepUiState()
    {
        return new UiState(new UiElementsVisibility(
                                                    MainMenu: UserSettings.Config.ShowMainMenu,
                                                    TitleAndDescription: UserSettings.Config.ShowTitleAndDescription,
                                                    GraphToolbar: UserSettings.Config.ShowToolbar,
                                                    Timeline: UserSettings.Config.ShowTimeline,
                                                    IsFocusMode: LayoutHandling.FocusMode,
                                                    InteractionOverlay: UserSettings.Config.ShowInteractionOverlay,
                                                    ShowMiniMap: UserSettings.Config.ShowMiniMap
                                                   ),
                           UserSettings.Config.WindowLayoutIndex,
                           UserSettings.Config.GraphStyle
                          )
            ;
    }

    internal static void ApplyUiState(UiState state)
    {
        ApplyUiVisibility(state.ElementsVisibility);
        UserSettings.Config.GraphStyle = state.GraphStyle;
        UserSettings.Config.WindowLayoutIndex = state.WindowLayoutIndex;
        LayoutHandling.LoadAndApplyLayoutOrFocusMode((LayoutHandling.Layouts)state.WindowLayoutIndex);
    }

    internal static void ApplyUiVisibility(UiElementsVisibility? visibility)
    {
        if (visibility == null)
            return;

        UserSettings.Config.ShowMainMenu = visibility.MainMenu;
        UserSettings.Config.ShowTitleAndDescription = visibility.TitleAndDescription;
        UserSettings.Config.ShowToolbar = visibility.GraphToolbar;
        UserSettings.Config.ShowTimeline = visibility.Timeline;
        LayoutHandling.FocusMode = visibility.IsFocusMode;
        UserSettings.Config.ShowInteractionOverlay = visibility.InteractionOverlay;
        UserSettings.Config.ShowMiniMap = visibility.ShowMiniMap;
    }

    internal sealed record UiElementsVisibility(
        bool MainMenu,
        bool TitleAndDescription,
        bool GraphToolbar,
        bool Timeline,
        bool IsFocusMode,
        bool InteractionOverlay,
        bool ShowMiniMap);

    internal sealed record UiState(
        UiElementsVisibility ElementsVisibility,
        int WindowLayoutIndex,
        UserSettings.GraphStyles GraphStyle);
}