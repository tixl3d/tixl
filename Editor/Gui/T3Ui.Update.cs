using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Resource;
using T3.Editor.App;
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
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.Skills.Training;
using T3.Editor.Skills.Data;
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
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        DragAndDropHandling.Update();

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

        if (UserSettings.Config.ShowMainMenu || UserSettings.Config.EnableMainMenuHoverPeek && ImGui.GetMousePos().Y < 3)
        {
            AppMenuBar.DrawAppMenuBar();
        }

        ThumbnailManager.Update();
        _searchDialog.Draw();
        NewProjectDialog.Draw();
        CreateFromTemplateDialog.Draw();
        _userNameDialog.Draw();
        AboutDialog.Draw();
        ExitDialog.Draw();
        SkillMapPopup.Draw();

        if (IsWindowLayoutComplete())
        {
            if (!UserSettings.IsUserNameDefined())
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

    private static void InitializeAfterAppWindowReady()
    {
        if (_initialed || ImGui.GetWindowSize() == Vector2.Zero)
            return;

        CompatibleMidiDeviceHandling.InitializeConnectedDevices();
        _initialed = true;
    }

    private static bool _initialed;

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