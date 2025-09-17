#nullable  enable
using System.Diagnostics;
using System.Threading.Tasks;
using ImGuiNET;
using Operators.Utils.Recording;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes.DataSet;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Resource;
using T3.Editor.App;
using T3.Editor.Compilation;
using T3.Editor.Gui.Dialog;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Midi;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Templates;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using T3.SystemUi;

namespace T3.Editor.Gui;

public static class T3Ui
{
    internal static void InitializeEnvironment()
    {
        //WindowManager.TryToInitialize();
        ExampleSymbolLinking.UpdateExampleLinks();

        Playback.Current = DefaultTimelinePlayback;
        ThemeHandling.Initialize();
    }

    internal static readonly Playback DefaultTimelinePlayback = new();
    internal static readonly BeatTimingPlayback DefaultBeatTimingPlayback = new();

    private static void InitializeAfterAppWindowReady()
    {
        if (_initialed || ImGui.GetWindowSize() == Vector2.Zero)
            return;


        CompatibleMidiDeviceHandling.InitializeConnectedDevices();
        _initialed = true;
    }

    private static bool _initialed;

    internal static void ProcessFrame()
    {
        Profiling.KeepFrameData();
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        DragAndDropHandling.Update();

        CustomComponents.BeginFrame();
        FormInputs.BeginFrame();
        InitializeAfterAppWindowReady();

        // Prepare the current frame 
        RenderStatsCollector.StartNewFrame();
        
        UpdateProjectsIfNecessary();

        if (!Playback.Current.IsRenderingToFile && ProjectView.Focused != null)
        {
            PlaybackUtils.UpdatePlaybackAndSyncing();
            AudioEngine.CompleteFrame(Playback.Current, Playback.LastFrameDuration);  
        }
        ScreenshotWriter.Update();
        
        ResourceManager.RaiseFileWatchingEvents();

        VariationHandling.Update();
        MouseWheelFieldWasHoveredLastFrame = MouseWheelFieldHovered;
        MouseWheelFieldHovered = false;

        // A work around for potential mouse capture
        DragFieldWasHoveredLastFrame = DragFieldHovered;
        DragFieldHovered = false;
        
        FitViewToSelectionHandling.ProcessNewFrame();
        SrvManager.RemoveForDisposedTextures();
        KeyActionHandling.InitializeFrame();
        
        CompatibleMidiDeviceHandling.UpdateConnectedDevices();

        var nodeSelection = ProjectView.Focused?.NodeSelection;
        if (nodeSelection != null)
        {
            // Set selected id so operator can check if they are selected or not  
            var selectedInstance = nodeSelection.GetSelectedInstanceWithoutComposition();
            MouseInput.SelectedChildId = selectedInstance?.SymbolChildId ?? Guid.Empty;
            InvalidateSelectedOpsForTransformGizmo(nodeSelection);
        }

        // Draw everything!
        ImGui.DockSpaceOverViewport();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
        WindowManager.Draw();
        ImGui.PopStyleVar();
            
        // Complete frame
        SingleValueEdit.StartNextFrame();
        
        
        FrameStats.CompleteFrame();
        TriggerGlobalActionsFromKeyBindings();

        if (UserSettings.Config.ShowMainMenu || ImGui.GetMousePos().Y < 3)
        {
            AppMenuBar.DrawAppMenuBar();
        }
            
        _searchDialog.Draw();
        NewProjectDialog.Draw();
        CreateFromTemplateDialog.Draw();
        _userNameDialog.Draw();
        AboutDialog.Draw();
        ExitDialog.Draw();

        if (IsWindowLayoutComplete())
        {
            if (!UserSettings.IsUserNameDefined() )
            {
                UserSettings.Config.UserName = Environment.UserName;
                _userNameDialog.ShowNextFrame();
            }
        }

        KeyboardAndMouseOverlay.Draw();

        Playback.OpNotReady = false;
        AutoBackup.AutoBackup.CheckForSave();

        Profiling.EndFrameData();
    }

    private static void UpdateProjectsIfNecessary()
    {
        foreach(var project in EditableSymbolProject.AllProjects)
        {
            project.Update(out var needsUpdating);
            if (needsUpdating)
            {
                _modifiedProjects.Add(project);
            }
        }

        if (_modifiedProjects.Count > 0)
        {
            var projects = _modifiedProjects.Cast<EditorSymbolPackage>().ToArray();
            ProjectSetup.UpdateSymbolPackages(projects);
        }
        
        _modifiedProjects.Clear();
    }

    private static void InvalidateSelectedOpsForTransformGizmo(NodeSelection nodeSelection)
    {
        // Keep invalidating selected op to enforce rendering of Transform gizmo  
        foreach (var si in nodeSelection.GetSelectedInstances().ToList())
        {
            if (si is not ITransformable)
                continue;

            foreach (var i in si.Inputs)
            {
                // Skip string inputs to prevent potential interference with resource file paths hooks
                // I.e. Invalidating these every frame breaks shader recompiling if Shader-op is selected
                if (i.ValueType != typeof(Vector3))
                {
                    continue;
                }

                i.DirtyFlag.ForceInvalidate();
            }
        }
    }

    /// <summary>
    /// This a bad workaround to defer some ui actions until we have completed all
    /// window initialization, so they are not discarded by the setup process.
    /// </summary>
    private static bool IsWindowLayoutComplete() => ImGui.GetFrameCount() > 2;

    private static void TriggerGlobalActionsFromKeyBindings()
    {
        if (KeyActionHandling.Triggered(UserActions.Undo))
        {
            UndoRedoStack.Undo();
        }
        else if (KeyActionHandling.Triggered(UserActions.Redo))
        {
            UndoRedoStack.Redo();
        }
        else if (KeyActionHandling.Triggered(UserActions.Save))
        {
            SaveInBackground(saveAll: false);
        }
        else if (KeyActionHandling.Triggered(UserActions.ToggleAllUiElements))
        {
            ToggleAllUiElements();
        }
        else if (KeyActionHandling.Triggered(UserActions.SearchGraph))
        {
            _searchDialog.ShowNextFrame();
        }
        else if (KeyActionHandling.Triggered(UserActions.ToggleFullscreen))
        {
            UserSettings.Config.FullScreen = !UserSettings.Config.FullScreen;
        }
        else if (KeyActionHandling.Triggered(UserActions.ToggleFocusMode)) ToggleFocusMode();
    }

    internal static void ToggleFocusMode()
    {
        var activeComponents = ProjectView.Focused;
        if (activeComponents == null)
            return;
        
        var shouldBeFocusMode = !UserSettings.Config.FocusMode;

        var outputWindow = OutputWindow.GetPrimaryOutputWindow();

        if (shouldBeFocusMode && outputWindow != null)
        {
            outputWindow.Pinning.TryGetPinnedOrSelectedInstance(out var instance, out _);
            activeComponents.GraphImageBackground.OutputInstance = instance;
        }

        UserSettings.Config.FocusMode = shouldBeFocusMode;
        UserSettings.Config.ShowToolbar = shouldBeFocusMode;
        ToggleAllUiElements();
        LayoutHandling.LoadAndApplyLayoutOrFocusMode(shouldBeFocusMode ? 11 : UserSettings.Config.WindowLayoutIndex);

        outputWindow = OutputWindow.GetPrimaryOutputWindow();
        if (!shouldBeFocusMode && outputWindow != null)
        {
            outputWindow.Pinning.PinInstance(activeComponents.GraphImageBackground.OutputInstance, activeComponents);
            activeComponents.GraphImageBackground.ClearBackground();
        }
    }

    internal static void ToggleAllUiElements()
    {
        //T3Ui.MaximalView = !T3Ui.MaximalView;
        if (UserSettings.Config.ShowToolbar)
        {
            UserSettings.Config.ShowMainMenu = false;
            UserSettings.Config.ShowTitleAndDescription = false;
            UserSettings.Config.ShowToolbar = false;
            UserSettings.Config.ShowTimeline = false;
        }
        else
        {
            UserSettings.Config.ShowMainMenu = true;
            UserSettings.Config.ShowTitleAndDescription = true;
            UserSettings.Config.ShowToolbar = true;
            if (Playback.Current.Settings.Syncing == PlaybackSettings.SyncModes.Timeline)
            {
                UserSettings.Config.ShowTimeline = true;
            }
        }
    }

    internal static void SaveInBackground(bool saveAll)
    {
        Task.Run(() => Save(saveAll));
    }

    internal static void Save(bool saveAll)
    {
        if (_saveStopwatch.IsRunning)
        {
            Log.Debug("Can't save modified while saving is in progress");
            return;
        }

        _saveStopwatch.Restart();

        // Todo - parallelize? 
        foreach (var package in EditableSymbolProject.AllProjects)
        {
            if (saveAll)
                package.SaveAll();
            else
                package.SaveModifiedSymbols();
        }

        _saveStopwatch.Stop();
    }

    internal static void SelectAndCenterChildIdInView(Guid symbolChildId)
    {
        var components = ProjectView.Focused;
        if (components == null || components.CompositionInstance == null)
            return;

        var compositionOp = components.CompositionInstance;

        var symbolUi = compositionOp.GetSymbolUi();
        
        if(!symbolUi.ChildUis.TryGetValue(symbolChildId, out var sourceChildUi))
            return;
        
        if(!compositionOp.Children.TryGetChildInstance(symbolChildId, out var selectionTargetInstance))
            return;
        
        components.NodeSelection.SetSelection(sourceChildUi, selectionTargetInstance);
        FitViewToSelectionHandling.FitViewToSelection();
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

    private static readonly Stopwatch _saveStopwatch = new();

    // ReSharper disable once InconsistentlySynchronizedField
    internal static bool IsCurrentlySaving => _saveStopwatch is { IsRunning: true };

    public static float UiScaleFactor { get; internal set; } = 1;
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
    public static bool ItemRegionsVisible;
    

}