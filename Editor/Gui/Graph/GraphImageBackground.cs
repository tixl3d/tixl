#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Editor.Gui.Interaction.Camera;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui;

/// <summary>
/// A helper class to render an image output into the background of the graph window 
/// </summary>
internal sealed class GraphImageBackground
{

    public bool IsActive => _backgroundNodePath != null;
    public bool HasInteractionFocus;

    public GraphImageBackground(NodeSelection nodeSelection, Structure structure)
    {
        //_window = window;
            
        _camSelectionHandling = new CameraSelectionHandling(nodeSelection, () => Playback.Current);
        _structure = structure;
        _nodeSelection = nodeSelection;
    }

    public Instance? OutputInstance
    {
        set => _backgroundNodePath = value?.InstancePath;
        get => _structure.GetInstanceFromIdPath(_backgroundNodePath);
    }

    private void DrawResolutionSelector()
    {
        ResolutionHandling.DrawSelector(ref _selectedResolution, null);
    }

    public void Draw(float imageOpacity)
    {
        if (_backgroundNodePath == null)
            return;

        // Prevent UiScaling for cropping image
        var keepScale = T3Ui.UiScaleFactor;
        T3Ui.UiScaleFactor = 1;

        DrawNonScaledCanvasContent(imageOpacity);
        T3Ui.UiScaleFactor = keepScale;
    }

    private void DrawNonScaledCanvasContent(float imageOpacity)
    {
        var selectedInstance = _nodeSelection.GetSelectedInstanceWithoutComposition();
        if (selectedInstance is ICamera camera)
        {
            _cameraInteraction.Update(camera, HasInteractionFocus);
        }

        _evaluationContext.ShowGizmos = T3.Core.Operator.GizmoVisibility.Off;
        _evaluationContext.Reset();
        _imageCanvas.SetViewMode(ImageOutputCanvas.Modes.Fitted);
        _imageCanvas.Update(T3Ui.EditingFlags.PreventMouseInteractions);

        var windowContentRegionMin = ImGui.GetWindowContentRegionMin() + new Vector2(0, 0);
        ImGui.SetCursorPos(windowContentRegionMin);

        var instanceForOutput = _structure.GetInstanceFromIdPath(_backgroundNodePath);

        if (instanceForOutput == null || instanceForOutput.Outputs.Count == 0)
            return;

        var viewOutput = instanceForOutput.Outputs[0];
        var preventCameraInteraction = !HasInteractionFocus
                                       || !string.IsNullOrEmpty(FrameStats.Last.OpenedPopUpName);
        _camSelectionHandling.Update(instanceForOutput, instanceForOutput.Outputs[0].ValueType, preventCameraInteraction);

        var viewSymbolUi = instanceForOutput.GetSymbolUi();
        if (!viewSymbolUi.OutputUis.TryGetValue(viewOutput.Id, out IOutputUi? viewOutputUi))
            return;

        _imageCanvas.SetAsCurrent();
        _evaluationContext.ShowGizmos = _showGizmos;
        _evaluationContext.RequestedResolution = _selectedResolution.ComputeResolution();
        _evaluationContext.SetDefaultCamera();
        if (_camSelectionHandling.CameraForRendering != null)
        {
            _evaluationContext.SetViewFromCamera(_camSelectionHandling.CameraForRendering);
        }

        var hackToHideResolution = UserSettings.Config.ShowToolbar;
        UserSettings.Config.ShowToolbar = false;
        viewOutputUi.DrawValue(viewOutput, _evaluationContext, "graphBackground", recompute: true);
        UserSettings.Config.ShowToolbar = hackToHideResolution;
            
        ImageOutputCanvas.Deactivate();

        if (imageOpacity < 1)
        {
            ImGui.GetWindowDrawList().AddRectFilled(Vector2.Zero, 
                                                    Vector2.One * 10000, 
                                                    UiColors.WindowBackground.Fade(1 - imageOpacity));
        }
    }

    public void DrawToolbarItems()
    {
        if (!IsActive)
            return;

        //ImGui.SameLine();
        if (ImGui.Button("Clear BG"))
        {
            ClearBackground();
        }

        ImGui.SameLine();
        DrawResolutionSelector();
        ImGui.SameLine();

        var showGizmos = _showGizmos != T3.Core.Operator.GizmoVisibility.Off;
        if (CustomComponents.ToggleIconButton(ref showGizmos, Icon.Grid, Vector2.One * ImGui.GetFrameHeight() * T3Ui.UiScaleFactor))
        {
            _showGizmos = showGizmos
                              ? T3.Core.Operator.GizmoVisibility.On
                              : T3.Core.Operator.GizmoVisibility.Off;
        }

        ImGui.SameLine();

        _camSelectionHandling.DrawCameraControlSelection();
        ImGui.SameLine();
    }

    public void ClearBackground()
    {
        _backgroundNodePath = null;
        HasInteractionFocus = false;
    }

    private GizmoVisibility _showGizmos;

    private readonly ImageOutputCanvas _imageCanvas = new();
    private readonly CameraSelectionHandling _camSelectionHandling;
    private readonly EvaluationContext _evaluationContext = new();
    private ResolutionHandling.Resolution _selectedResolution = ResolutionHandling.DefaultResolution;
    private readonly CameraInteraction _cameraInteraction = new();
    private IReadOnlyList<Guid>? _backgroundNodePath;
    //private readonly GraphWindow _window;
    private readonly Structure _structure;
    private readonly NodeSelection _nodeSelection;

}