#nullable enable

using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Video;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using SkillTraining = T3.Editor.Skills.Training.SkillTraining;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

[HelpUiID("OutputWindow")]
internal sealed partial class OutputWindow : Window
{
    #region Window implementation
    public OutputWindow()
    {
        Config.Title = LayoutHandling.OutputPrefix + _instanceCounter;
        Config.Visible = true;

        AllowMultipleInstances = true;
        Config.Visible = true;
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        _instanceCounter++;
        _camSelectionHandling = new CameraSelectionHandling();
        _drawOutputMenuExtras = DrawOutputMenuExtras;
        OutputWindowInstances.Add(this);
    }

    /// <summary>Resets the view camera - used by the debug protocol so agent sessions can reframe the origin.</summary>
    internal void ResetView()
    {
        _camSelectionHandling.ResetView();
    }

    public static bool TryGetPrimaryOutputWindow([NotNullWhen(true)] out OutputWindow? outputWindow)
    {
        foreach (var window in OutputWindowInstances)
        {
            if (!window.Config.Visible)
                continue;

            if (window is not OutputWindow outputWindow2)
                 continue;

            outputWindow = outputWindow2;
            return true;

        }

        outputWindow = null;
        return false;
    }

    public Texture2D? GetCurrentTexture()
    {
        return _imageCanvas.LastTexture;
    }

    protected override void Close()
    {
        SaveStateToProject();
        OutputWindowInstances.Remove(this);
    }

    protected override void AddAnotherInstance()
    {
        // ReSharper disable once ObjectCreationAsStatement
        new OutputWindow();
    }

    internal override List<Window> GetInstances()
    {
        return OutputWindowInstances;
    }
    #endregion

    protected override void DrawContent()
    {
        SyncStateWithProject();

        // In skill training the toolbar is hidden, so force the window-fitting resolution
        // instead of whatever the level project saved (typically 1920x1080).
        if (SkillTraining.IsInPlayMode && _selectedResolution != ResolutionHandling.DefaultResolution)
            _selectedResolution = ResolutionHandling.DefaultResolution;

        // Sync copy-based fields to State every frame so saves always capture current values
        SyncCopyFieldsToState();

        Pinning.TryGetPinnedOrSelectedInstance(out var drawnInstance, out var graphCanvas);
        _setupMode.DrawSidePanel();

        ImGui.BeginChild("##content",
                         new Vector2(0, ImGui.GetWindowHeight()),
                         ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoScrollbar |
                         ImGuiWindowFlags.NoMove |
                         ImGuiWindowFlags.NoScrollWithMouse |
                         ImGuiWindowFlags.NoBackground
                        );
        {
            // Very ugly hack to prevent scaling the output above window size
            var keepScale = T3Ui.UiScaleFactor;

            // Draw output
            _imageCanvas.SetAsCurrent();

            // Move down to avoid overlapping with the toolbar
            ImGui.SetCursorPos(ImGui.GetCursorStartPos() + new Vector2(0, 40));
            // ImGui 1.91 sets an internal IsSetPos flag on SetCursorPos and asserts in End()
            // if no item is submitted afterwards. The image canvas draws to the raw draw list
            // and does not emit items, so submit an empty Dummy as an extent marker.
            ImGui.Dummy(Vector2.Zero);

            if (_setupMode.TryDrawEditingView(drawnInstance, EvaluationContext))
            {
                // Output-editing view (focused sink or picked panel entity) was drawn — give it the
                // breadcrumb so the setup panel and op selection stay reachable while editing.
                if (!SkillTraining.IsInPlayMode)
                {
                    ImGui.SetCursorPos(ImGui.GetCursorStartPos());
                    CustomComponents.PushToolbarIconBackground();
                    _setupMode.DrawPanelToggleButton();
                    _setupMode.DrawPinIndicator();
                    Pinning.DrawPinning(_drawOutputMenuExtras);
                    CustomComponents.PopToolbarIconBackground();
                }
            }
            else if (graphCanvas != null)
            {
                Pinning.TryGetPinnedEvaluationInstance(graphCanvas.Structure, out var evaluationInstance);

                var drawnType = UpdateAndDrawOutput(drawnInstance, evaluationInstance);
                ImageOutputCanvas.Deactivate();
                _camSelectionHandling.Update(drawnInstance, drawnType);
                var editingFlags = _camSelectionHandling.PreventCameraInteraction 
                                   | _camSelectionHandling.PreventImageCanvasInteraction
                                   | SkillTraining.IsInPlayMode
                                   | drawnType != typeof(Texture2D)
                                       ? T3Ui.EditingFlags.PreventMouseInteractions
                                       : T3Ui.EditingFlags.None;

                if ((editingFlags & T3Ui.EditingFlags.PreventMouseInteractions) != 0)
                    T3Ui.UiScaleFactor = 1;
                
                _imageCanvas.Update(editingFlags);

                T3Ui.UiScaleFactor = keepScale;

                if (UserActions.FocusSelection.Triggered())
                {
                    if (drawnType == typeof(Texture2D))
                    {
                        _imageCanvas.SetViewMode(ImageOutputCanvas.Modes.Fitted);
                    }
                    else if (drawnType == typeof(Command))
                    {
                        _camSelectionHandling.ResetView();
                    }
                }

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
                ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.BackgroundHover.Rgba);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundHover.Rgba);
                ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.3f, 0.3f, 0.3f, 0.1f));

                if (!SkillTraining.IsInPlayMode)
                {
                    DrawToolbar(drawnType);
                    DrawRenderProgressBar();
                }
                
                ImGui.PopStyleColor(6);
            }

            CustomComponents.DrawWindowFocusFrame();
        }
        ImGui.EndChild();
    }


    public static readonly List<Window> OutputWindowInstances = [];
    public ViewSelectionPinning Pinning { get; } = new();
    private readonly OutputSetupModeView _setupMode = new();

    private System.Numerics.Vector4 _backgroundColor = new(0.1f, 0.1f, 0.1f, 1.0f);
    internal readonly EvaluationContext EvaluationContext = new();
    private readonly ImageOutputCanvas _imageCanvas = new();
    private readonly CameraSelectionHandling _camSelectionHandling;
    private readonly System.Action _drawOutputMenuExtras;
    private static int _instanceCounter;
    private ResolutionHandling.Resolution _selectedResolution = ResolutionHandling.DefaultResolution;
    internal Int2 RequestedResolution { get; private set; }
    private readonly EditResolutionDialog _resolutionDialog = new();

}
