#nullable enable
using ImGuiNET;
using T3.Editor.App;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// P shows the selected operator in the output window (in focus mode: as the graph background). A tap keeps it
/// pinned; holding P only peeks at it and brings the previous output back on release. Pressing P again on the
/// pinned operator keeps it pinned instead of unpinning it.
/// </summary>
internal static class OutputPeek
{
    internal static void Handle(GraphUiContext context)
    {
        if (UserActions.PinToOutputWindow.Triggered())
        {
            Begin(context);
            return;
        }

        if (!_isKeyDown || IsPinKeyDown())
            return;

        _isKeyDown = false;
        var wasHeld = ImGui.GetTime() - _pressTime > TapMaxSec;
        if (wasHeld)
            End(context);
        else
            _peekingWindow?.Pinning.CommitPeek();

        _peekingWindow = null;
        _previousBackgroundPath = null;
    }

    private static void Begin(GraphUiContext context)
    {
        var projectView = ProjectView.Focused;
        if (projectView == null)
            return;

        _isKeyDown = true;
        _pressTime = ImGui.GetTime();

        if (LayoutHandling.FocusMode)
        {
            var selectedInstance = context.Selector.GetFirstSelectedInstance();
            if (selectedInstance == null)
                return;

            _previousBackgroundPath = projectView.GraphImageBackground.OutputInstance?.InstancePath;
            _hadBackground = _previousBackgroundPath != null;
            projectView.SetBackgroundOutput(selectedInstance);
            return;
        }

        _peekingWindow = GetVisibleOutputWindow();
        _peekingWindow?.Pinning.BeginPeek();
        NodeActions.PinSelectedToOutputWindow(projectView, context.Selector, context.CompositionInstance);
    }

    private static void End(GraphUiContext context)
    {
        if (LayoutHandling.FocusMode)
        {
            var projectView = ProjectView.Focused;
            if (projectView == null)
                return;

            var previous = _previousBackgroundPath != null ? projectView.Structure.GetInstanceFromIdPath(_previousBackgroundPath) : null;
            if (previous != null)
                projectView.SetBackgroundOutput(previous);
            else if (!_hadBackground)
                projectView.GraphImageBackground.ClearBackground();

            return;
        }

        _peekingWindow?.Pinning.EndPeek();
    }

    private static OutputWindow? GetVisibleOutputWindow()
    {
        foreach (var window in OutputWindow.OutputWindowInstances)
        {
            if (window.Config.Visible && window is OutputWindow outputWindow)
                return outputWindow;
        }

        return null;
    }

    /** Checks the key alone, so pressing a modifier while holding P doesn't end the peek */
    private static bool IsPinKeyDown()
    {
        foreach (var binding in KeyMapSwitching.CurrentKeymap.Bindings)
        {
            if (binding.Action == UserActions.PinToOutputWindow && ImGui.IsKeyDown(binding.KeyCombination.Key.ToImGuiKey()))
                return true;
        }

        return false;
    }

    /** Releasing P later than this counts as holding it */
    private const double TapMaxSec = 0.25;

    private static bool _isKeyDown;
    private static double _pressTime;
    private static OutputWindow? _peekingWindow;
    private static IReadOnlyList<Guid>? _previousBackgroundPath;
    private static bool _hadBackground;
}
