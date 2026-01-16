#nullable enable
using ImGuiNET;
using Operators.Utils.Recording;
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Editor.Gui.Dialog;
using T3.Editor.Gui.Dialogs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Templates;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui;

public static partial class T3Ui
{
    internal static void InitializeEnvironment()
    {
        ExampleSymbolLinking.UpdateExampleLinks();

        Playback.Current = DefaultTimelinePlayback;
        ThemeHandling.Initialize();
    }

    internal static readonly Playback DefaultTimelinePlayback = new();
    internal static readonly BeatTimingPlayback DefaultBeatTimingPlayback = new();

    /// <summary>
    /// This a bad workaround to defer some ui actions until we have completed
    /// window initialization, so they are not discarded by the setup process.
    /// </summary>
    private static bool IsWindowLayoutComplete() => ImGui.GetFrameCount() > 2;

    private static void TriggerGlobalActionsFromKeyBindings()
    {
        if (UserActions.Undo.Triggered())
        {
            UndoRedoStack.Undo();
        }
        else if (UserActions.Redo.Triggered())
        {
            UndoRedoStack.Redo();
        }
        else if (UserActions.Save.Triggered())
        {
            SaveInBackground(saveAll: false);
        }
        else if (UserActions.ToggleAllUiElements.Triggered())
        {
            UiConfig.ToggleAllUiElements();
        }
        else if (UserActions.SearchGraph.Triggered())
        {
            _searchDialog.ShowNextFrame();
        }
        else if (UserActions.ToggleFullscreen.Triggered())
        {
            UserSettings.Config.FullScreen = !UserSettings.Config.FullScreen;
        }
        else if (UserActions.ToggleFocusMode.Triggered())
            UiConfig.ToggleFocusMode();
    }

    /// <summary>
    /// Statistics method for debug purpose
    /// </summary>
    // private static void CountSymbolUsage()
    // {
    //     var counts = new Dictionary<Symbol, int>();
    //     foreach (var s in EditorSymbolPackage.AllSymbols)
    //     {
    //         foreach (var child in s.Children.Values)
    //         {
    //             counts.TryAdd(child.Symbol, 0);
    //             counts[child.Symbol]++;
    //         }
    //     }
    //
    //     foreach (var (s, c) in counts.OrderBy(c => counts[c.Key]).Reverse())
    //     {
    //         Log.Debug($"{s.Name} - {s.Namespace}  {c}");
    //     }
    // }

    //@imdom: needs clarification how to handle osc data disconnection on shutdown
    // public void Dispose()
    // {
    //     GC.SuppressFinalize(this);
    //     OscDataRecording.Dispose();
    // }

    internal static bool MouseWheelFieldHovered { private get; set; }

    internal static bool MouseWheelFieldWasHoveredLastFrame { get; private set; }
    internal static bool DragFieldHovered { private get; set; }
    internal static bool DragFieldWasHoveredLastFrame { get; private set; }

    internal static bool ShowSecondaryRenderWindow => WindowManager.ShowSecondaryRenderWindow;
    internal const string FloatNumberFormat = "{0:F2}";

    // ReSharper disable once InconsistentlySynchronizedField

    internal static float UiScaleFactor { get; set; } = 1;
    internal static float DisplayScaleFactor { get; set; } = 1;
    internal static bool IsAnyPopupOpen => !string.IsNullOrEmpty(FrameStats.Last.OpenedPopUpName);

    internal static readonly MidiDataRecording MidiDataRecording = new(DataRecording.ActiveRecordingSet);
    internal static readonly OscDataRecording OscDataRecording = new(DataRecording.ActiveRecordingSet);

    internal static readonly CreateFromTemplateDialog CreateFromTemplateDialog = new();
    private static readonly UserNameDialog _userNameDialog = new();
    internal static readonly AboutDialog AboutDialog = new();
    private static readonly SearchDialog _searchDialog = new();
    internal static readonly NewProjectDialog NewProjectDialog = new();
    internal static readonly ExitDialog ExitDialog = new();
    private static readonly List<EditableSymbolProject> _modifiedProjects = new();

    [Flags]
    public enum EditingFlags
    {
        None = 0,
        ExpandVertically = 1 << 1,
        PreventMouseInteractions = 1 << 2,
        PreventZoomWithMouseWheel = 1 << 3,
        PreventPanningWithMouse = 1 << 4,
        AllowHoveredChildWindows = 1 << 5,
    }

    internal static bool UseVSync = true;
}