using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Resource;
using T3.Editor.App;
using T3.Editor.Gui.Dialog;
using T3.Editor.Compilation;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Midi;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.Skills.Training;
using T3.Editor.Skills.Ui;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using T3.SystemUi;

namespace T3.Editor.Gui;

public static partial class T3Ui
{
    internal static void ProcessFrame()
    {
        Profiling.KeepFrameData();
        App.DebugProtocol.DebugServer.ProcessMainThreadQueue();
        //ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        DragAndDropHandling.Update();
        MagGraph.Ui.DropHandling.DrawDragIndicator();

        CustomComponents.BeginFrame();
        FormInputs.BeginFrame();
        InitializeAfterAppWindowReady();

        MouseWheelPanning.ProcessFrame(120);
        
        // Prepare the current frame 
        RenderStatsCollector.StartNewFrame();
        
        UpdateModifiedProjects();

        if (!Playback.Current.IsRenderingToFile && ProjectView.Focused != null)
        {
            PlaybackUtils.UpdatePlaybackAndSyncing();
            AudioEngine.CompleteFrame(Playback.Current, Playback.LastFrameDuration);
        }
        else if (ProjectView.Focused != null)
        {
            // Routing still has to happen while rendering. A source that isn't wired into an [AudioBus] reaches
            // the mix only through the implicit default bus, so without this it would be decoded and fed but
            // never routed — silent in the rendered file while audible live. (The collector's own transport
            // gate already treats recording as running, so it expects to be called here.)
            AudioGraphCollector.CollectLooseSources(ProjectView.Focused.CompositionInstance);
        }

        ScreenshotWriter.Update();
        RenderProcess.Update();
        SkillTraining.Update();
        SkillMapEditor.Draw();

        ResourcePackageManager.RaiseFileWatchingEvents();

        VariationHandling.Update();
        MouseWheelFieldWasHoveredLastFrame = MouseWheelFieldHovered;
        MouseWheelFieldHovered = false;

        // A workaround for potential mouse capture
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
            NodeSelection.InvalidateSelectedOpsForTransformGizmo(nodeSelection);
        }

        // Draw everything!
        ImGui.DockSpaceOverViewport();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
        WindowManager.Draw();
        ImGui.PopStyleVar();

        // Complete frame
        SingleValueEdit.StartNextFrame();

        SkillTraining.PostUpdate();

        FrameStats.CompleteFrame();
        TriggerGlobalActionsFromKeyBindings();

        PlaybackTimeScrubHandling.ProcessFrame();

        //if (UserSettings.Config.ShowMainMenu || UserSettings.Config.EnableMainMenuHoverPeek && ImGui.GetMousePos().Y < 3)
        if (UserSettings.Config.ShowMainMenu || ImGui.GetMousePos().Y < 3)
        {
            AppMenuBar.DrawAppMenuBar();
        }

        ThumbnailManager.Update();
        _searchDialog.Draw();
        NewProjectDialog.Draw();
        ShareProjectDialog.Draw();
        RestoreBackupDialog.Draw();
        _couldNotLoadProjectDialog.Draw();
        CreateFromTemplateDialog.Draw();
        _userNameDialog.Draw();
        Windows.AssetLib.FolderImportDialog.Instance.Draw();
        AboutDialog.Draw();
        ExitDialog.Draw();
        OperatorHelp.EditDescriptionDialog.Draw();
        SkillMapPopup.Draw();

        // Classify the launch first so the welcome window claims the foreground before the
        // user-name prompt. They must not overlap — the user-name dialog waits until the
        // welcome window is closed (see the IsVisible guard below).
        CheckForVersionWelcome();

        if (IsWindowLayoutComplete() && _versionWelcomeChecked && !WindowManager.WelcomeWindow.IsVisible)
        {
            if (!UserSettings.IsUserNameDefined())
            {
                UserSettings.Config.UserName = Environment.UserName;
                _userNameDialog.ShowNextFrame();
            }
            else if (!_blockedProjectsChecked && !IsAnyPopupOpen)
            {
                // Waits for the startup dialogs above to close, then warns once per launch about
                // projects a sync tool blocked from loading.
                _blockedProjectsChecked = true;
                _couldNotLoadProjectDialog.ShowIfProjectsBlocked();
            }
        }

        KeyboardAndMouseOverlay.Draw();

        Playback.OpNotReady = false;
        AutoBackup.AutoBackup.CheckForSave();

        Profiling.EndFrameData();
    }

    private static void InitializeAfterAppWindowReady()
    {
        if (_initialed || ImGui.GetWindowSize() == Vector2.Zero)
            return;

        CompatibleMidiDeviceHandling.InitializeConnectedDevices();
        _initialed = true;
    }

    private static bool _initialed;

    /// <summary>
    /// Once per launch, after the layout is up and no other popup is open, opens the welcome window if
    /// the user keeps it on startup, or if this is a version they haven't run in this folder before.
    /// Otherwise just records the run so the next new version is detected. Waits for any startup popup
    /// (e.g. user name) to close first.
    /// </summary>
    private static void CheckForVersionWelcome()
    {
        if (_versionWelcomeChecked || !IsWindowLayoutComplete() || IsAnyPopupOpen)
            return;

        _versionWelcomeChecked = true;

        var isNewVersion = VersionMarker.Classify() == VersionMarker.LaunchKind.NewToUser;

        // Snapshot existing projects before the first save can upgrade them to the new file format.
        // Runs before the marker is written (on welcome-window close) so a crash mid-pass retries.
        if (isNewVersion)
            AutoBackup.PreUpgradeSnapshot.CreateForExistingProjects();

        if (UserSettings.Config.ShowWelcomeOnStartup || isNewVersion)
            WindowManager.WelcomeWindow.Open();
        else
            VersionMarker.MarkCurrentVersionSeen();
    }

    private static bool _versionWelcomeChecked;
    private static bool _blockedProjectsChecked;

    private static void UpdateModifiedProjects()
    {
        foreach (var project in EditableSymbolProject.AllProjects)
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
}