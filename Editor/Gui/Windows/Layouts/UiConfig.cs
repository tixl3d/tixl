#nullable enable
using T3.Core.Animation;
using T3.Core.Operator;
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
            
        var activeComponents = ProjectView.Focused;
        if (activeComponents == null)
            return;

        var shouldBeFocusMode = !UserSettings.Config.FocusMode;

        if (shouldBeFocusMode)
        {
            if (!OutputWindow.TryGetPrimaryOutputWindow(out var oldOutputWindow))
                return;

            oldOutputWindow.Pinning.TryGetPinnedOrSelectedInstance(out var instance, out _);
            activeComponents.GraphImageBackground.OutputInstance = instance;
        }

        UserSettings.Config.FocusMode = shouldBeFocusMode;
        UserSettings.Config.ShowToolbar = shouldBeFocusMode;
        
        ToggleAllUiElements();

        LayoutHandling.LoadAndApplyLayoutOrFocusMode(shouldBeFocusMode
                                                         ? LayoutHandling.Layouts.FocusMode
                                                         : (LayoutHandling.Layouts)UserSettings.Config.WindowLayoutIndex);

        if (!OutputWindow.TryGetPrimaryOutputWindow(out var newOutputWindow))
            return;
        
        if (!shouldBeFocusMode)
        {
            newOutputWindow.Pinning.PinInstance(activeComponents.GraphImageBackground.OutputInstance, activeComponents);
            activeComponents.GraphImageBackground.ClearBackground();
        }
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

    private static void ShowAllUiElements()
    {
        UserSettings.Config.ShowMainMenu = true;
        UserSettings.Config.ShowTitleAndDescription = true;
        UserSettings.Config.ShowToolbar = true;
        if (Playback.Current.Settings != null && Playback.Current.Settings.Syncing == PlaybackSettings.SyncModes.Timeline)
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
        UserSettings.Config.EnableMainMenuHoverPeek = false;
    }

    internal static UiElementsVisibility KeepUiState()
    {
        return new UiElementsVisibility(UserSettings.Config.ShowMainMenu,
                                        UserSettings.Config.EnableMainMenuHoverPeek,
                                        UserSettings.Config.ShowTitleAndDescription,
                                        UserSettings.Config.ShowToolbar,
                                        UserSettings.Config.ShowTimeline,
                                        UserSettings.Config.WindowLayoutIndex,
                                        UserSettings.Config.FocusMode,
                                        UserSettings.Config.ShowInteractionOverlay,
                                        UserSettings.Config.ShowMiniMap,
                                        UserSettings.Config.GraphStyle
                                       );
    }
    
    internal static void ApplyUiState(UiElementsVisibility? state)
    {
        if (state == null)
            return;
        
        UserSettings.Config.ShowMainMenu= state.MainMenu;
        UserSettings.Config.EnableMainMenuHoverPeek = state.MainMenu;
        UserSettings.Config.ShowTitleAndDescription= state.TitleAndDescription;
        UserSettings.Config.ShowToolbar= state.GraphToolbar;
        UserSettings.Config.ShowTimeline= state.Timeline;
        UserSettings.Config.FocusMode= state.IsFocusMode;
        UserSettings.Config.ShowInteractionOverlay = state.InteractionOverlay;
        UserSettings.Config.GraphStyle = state.GraphStyle;
        UserSettings.Config.ShowMiniMap = state.ShowMiniMap;
        
        UserSettings.Config.WindowLayoutIndex= state.WindowLayoutIndex;
        LayoutHandling.LoadAndApplyLayoutOrFocusMode((LayoutHandling.Layouts)state.WindowLayoutIndex);
    }

    internal sealed record UiElementsVisibility(
        bool MainMenu,
        bool EnableMainMenuPeek,
        bool TitleAndDescription,
        bool GraphToolbar,
        bool Timeline,
        int WindowLayoutIndex,
        bool IsFocusMode,
        bool InteractionOverlay,
        bool ShowMiniMap,
        UserSettings.GraphStyles GraphStyle);
}