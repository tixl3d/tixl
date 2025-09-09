#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Editor.Gui.Interaction.Camera;
using T3.Editor.Gui.Interaction.TransformGizmos;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Handles switching and pinning of interactive <see cref="ICamera"/>s for views.
/// It uses four difference modes, draws the dropdown to switch between them
/// </summary>
internal sealed class CameraSelectionHandling
{
    public ICamera? CameraForRendering { get; private set; }
    public bool BypassCamera { get; private set; }

    public bool PreventCameraInteraction { get; private set; }

    public bool PreventImageCanvasInteraction { get; private set; }

    private enum ControlModes
    {
        /// <summary>
        /// If selected op is Camera, manipulate this.
        /// else
        ///   if playback is running
        ///      move ViewerCamera to first camera in graph  
        ///   else
        ///      manipulate ViewerCamera
        /// </summary>
        SceneViewerFollowing,

        /// <summary>
        /// Always use 3rd person view of ViewerCamera, regardless of selected Operator.
        /// </summary>
        UseViewer,

        /// <summary>
        /// If rendered op is Command-Type and manipulate first found camera in graph
        /// or if rendered Op is ImageType and selected Op is Camera manipulate camera 
        /// </summary>
        AutoUseFirstCam,

        /// <summary>
        /// Uses the camera picked from dropdown for manipulation.
        /// If camera wasn't recently updated, fall back to last other mode. 
        /// </summary>
        PickedACamera,
    }

    public CameraSelectionHandling()
    {
        _getPlayback = () => Playback.Current;
    }

    public CameraSelectionHandling(NodeSelection nodeSelection, Func<Playback> getPlayback)
    {
        _nodeSelection = nodeSelection;
        _getPlayback = getPlayback;
    }

    private readonly NodeSelection? _nodeSelection;

    // preparation for each window to have its own playback
    private readonly Func<Playback> _getPlayback;

    private NodeSelection? NodeSelection => _nodeSelection ?? ProjectView.Focused?.NodeSelection;

    public void Update(Instance? drawnInstance, Type drawnType, bool preventInteractions = false)
    {
        var currentPlayback = _getPlayback();
        var timeInBars = currentPlayback.TimeInBars;
        _hasAnimationTimeChanged = Math.Abs(_lastUpdateTime - timeInBars) > 0.001f;
        _lastUpdateTime = timeInBars;
        PreventImageCanvasInteraction = false;

        _drawnInstance = drawnInstance;
        if (drawnInstance == null)
        {
            PreventCameraInteraction = true;
            CameraForRendering = _outputWindowViewCamera;
            _recentlyUsedCameras.Clear();
            return;
        }

        // Update recently used cameras (this is expensive!)
        UpdateRecentCameras(drawnInstance);

        ICamera? cameraForManipulation = null;
        CameraForRendering = null;

        PreventCameraInteraction = preventInteractions;

        if (_controlMode == ControlModes.PickedACamera)
        {
            var isPickStillValid = false;
            foreach (var recentCam in _recentlyUsedCameras)
            {
                if (recentCam is not Instance camInstance)
                    continue;

                if (camInstance.SymbolChildId == _pickedCameraId)
                {
                    cameraForManipulation = recentCam;
                    CameraForRendering = _outputWindowViewCamera;
                    isPickStillValid = true;
                    break;
                }
            }

            if (!isPickStillValid)
            {
                _controlMode = _lastControlMode;
            }
        }

        _drawnTypeIsCommand = drawnType == typeof(Command);
        switch (_controlMode)
        {
            case ControlModes.SceneViewerFollowing:
            {
                if (!_drawnTypeIsCommand)
                {
                    PreventCameraInteraction = true;
                }

                cameraForManipulation = _outputWindowViewCamera;

                if (_firstCamInGraph != null)
                {
                    PreventImageCanvasInteraction = true;
                    var isCamOpSelected = IsCamOpSelected();
                    if (isCamOpSelected)
                    {
                        cameraForManipulation = _firstCamInGraph;
                    }

                    if (_hasAnimationTimeChanged)
                    {
                        _outputWindowViewCamera.CameraPosition = _firstCamInGraph.CameraPosition;
                        _outputWindowViewCamera.CameraTarget = _firstCamInGraph.CameraTarget;
                        _outputWindowViewCamera.CameraRoll = _firstCamInGraph.CameraRoll;
                    }
                }

                BypassCamera = true;
                break;
            }

            case ControlModes.UseViewer:
                cameraForManipulation = _outputWindowViewCamera;
                BypassCamera = true;
                break;

            case ControlModes.AutoUseFirstCam:
            {
                if (_firstCamInGraph != null)
                {
                    var isCamOpSelected = IsCamOpSelected();
                    if (!isCamOpSelected)
                    {
                        cameraForManipulation = _outputWindowViewCamera;
                    }
                    else
                    {
                        PreventImageCanvasInteraction = true;
                        cameraForManipulation = _firstCamInGraph;
                    }

                    CameraForRendering = _outputWindowViewCamera;
                    CameraInteraction.ResetCamera(_outputWindowViewCamera);
                }
                else
                {
                    cameraForManipulation = _outputWindowViewCamera;
                }

                BypassCamera = false;
                break;
            }
        }

        if (_controlMode != ControlModes.PickedACamera)
        {
            CameraForRendering = cameraForManipulation;
        }
        else
        {
            PreventImageCanvasInteraction = true;
        }

        CameraForRendering ??= _outputWindowViewCamera;

        if (TransformGizmoHandling.IsDragging)
            PreventCameraInteraction = true;

        if (cameraForManipulation != null)
        {
            _cameraInteraction.Update(cameraForManipulation, !PreventCameraInteraction);
        }

        bool IsCamOpSelected()
        {
            var firstSelectedInstance = NodeSelection?.GetSelectedInstanceWithoutComposition();
            return firstSelectedInstance is ICamera camera && camera == _firstCamInGraph;
        }
    }

    /// <summary>
    /// Searches for all recently updated (evaluated) camera ops in the current operator 
    /// </summary>
    private void UpdateRecentCameras(Instance drawnInstance)
    {
        _recentlyUsedCameras.Clear();

        var parentInstance = drawnInstance.Parent;
        if (parentInstance != null)
        {
            var children = parentInstance.Children.Values;
            foreach (var child in children)
            {
                if (child is not ICamera cam2)
                    continue;

                if (child.Outputs[0].DirtyFlag.FramesSinceLastUpdate > 1)
                    continue;

                _recentlyUsedCameras.Add(cam2);
            }
        }

        var selectedInstance = NodeSelection?.GetSelectedInstanceWithoutComposition();
        if (selectedInstance is ICamera selectedCamera
            && !_recentlyUsedCameras.Contains(selectedCamera))
        {
            _recentlyUsedCameras.Add(selectedCamera);
        }

        _firstCamInGraph = _recentlyUsedCameras.Count > 0 ? _recentlyUsedCameras[0] : null;
    }

    public void DrawCameraControlSelection()
    {
        if (_drawnInstance == null)
            return;

        ImGui.SetNextItemWidth(145*T3Ui.UiScaleFactor);

        var label = String.Empty;

        switch (_controlMode)
        {
            case ControlModes.AutoUseFirstCam:
                label = "Auto";
                break;

            case ControlModes.SceneViewerFollowing:
                label = "Follow";
                break;

            case ControlModes.UseViewer:
                label = "Viewer";
                break;

            case ControlModes.PickedACamera:
                label = "Locked to Op";
                break;
        }

        var isAuto = _controlMode == ControlModes.AutoUseFirstCam;

        var width = ImGui.GetFrameHeight() * 5;
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, Vector2.Zero);
        var labelColor = isAuto ? UiColors.TextMuted : UiColors.ForegroundFull;
        ImGui.PushStyleColor(ImGuiCol.Text, labelColor.Rgba);
        var isClicked = ImGui.Button($"     {label}##CameraMode", new Vector2(width, ImGui.GetFrameHeight()));
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (isClicked)
        {
            ImGui.OpenPopup("cameraPopup");
            ImGui.SetNextWindowPos(new Vector2(min.X, max.Y));
        }

        Icons.DrawIconAtScreenPosition(Icon.Camera, min + new Vector2(5, 7) * T3Ui.UiScaleFactor, ImGui.GetWindowDrawList(), labelColor);

        if (ImGui.BeginPopup("cameraPopup", ImGuiWindowFlags.None))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6) * T3Ui.UiScaleFactor);
            if (ImGui.MenuItem("Auto", "", _controlMode == ControlModes.AutoUseFirstCam, true))
            {
                _controlMode = ControlModes.AutoUseFirstCam;
                _pickedCameraId = Guid.Empty;
            }

            CustomComponents.TooltipForLastItem("Shows the first scene camera. For manipulation select the camera operator in graph or in the list below.");
            if (ImGui.IsItemHovered() && _firstCamInGraph is Instance camInstance && NodeSelection != null)
                FrameStats.AddHoveredId(camInstance.SymbolChildId);

            if (ImGui.MenuItem("Viewer",
                               "",
                               _controlMode == ControlModes.UseViewer,
                               _drawnTypeIsCommand))
            {
                _controlMode = ControlModes.UseViewer;
                _pickedCameraId = Guid.Empty;
            }

            CustomComponents.TooltipForLastItem("Ignores scene cameras. This can be useful in combination with [ShowCamGizmos].");

            if (ImGui.MenuItem("Viewer (Following)",
                               "",
                               _controlMode == ControlModes.SceneViewerFollowing,
                               _drawnTypeIsCommand
                              ))
            {
                _controlMode = ControlModes.SceneViewerFollowing;
                _pickedCameraId = Guid.Empty;
            }

            if (ImGui.IsItemHovered() && _firstCamInGraph is Instance camInstance2 && NodeSelection != null)
                FrameStats.AddHoveredId(camInstance2.SymbolChildId);

            CustomComponents
               .TooltipForLastItem("During playback the scene viewer is following the scene camera. Otherwise it can be independently manipulated without affecting the scene camera.");

            if (_recentlyUsedCameras.Count > 0)
            {
                ImGui.Separator();
                CustomComponents.HintLabel("Active Cameras...");
            }

            if (_drawnInstance?.Parent != null)
            {
                foreach (var cam in _recentlyUsedCameras)
                {
                    if (cam is not Instance cameraInstance ||
                        !_drawnInstance.Parent.Symbol.Children.TryGetValue(cameraInstance.SymbolChildId, out var symbolChild))
                        continue;

                    ImGui.PushID(cameraInstance.SymbolChildId.GetHashCode());
                    {
                        if (symbolChild != null && ImGui.MenuItem(symbolChild.ReadableName, "", cameraInstance.SymbolChildId == _pickedCameraId, true))
                        {
                            _lastControlMode = _controlMode;
                            _controlMode = ControlModes.PickedACamera;
                            _pickedCameraId = cameraInstance.SymbolChildId;
                            T3Ui.SelectAndCenterChildIdInView(symbolChild.Id);
                        }

                        if (ImGui.IsItemHovered() && NodeSelection != null)
                        {
                            FrameStats.AddHoveredId(cameraInstance.SymbolChildId);
                        }
                    }
                    ImGui.PopID();
                }
            }

            ImGui.PopStyleVar();
            ImGui.EndPopup();
        }
        else
        {
            CustomComponents.TooltipForLastItem("Camera control mode",
                                                "This affects which camera will be manipulated by the view controls. Please also review to the tooltips of the dropdown options.");
        }

        ImGui.SameLine();
    }

    public void ResetView()
    {
        _cameraInteraction.ResetView();
    }

    private bool _drawnTypeIsCommand;
    private ControlModes _controlMode = ControlModes.AutoUseFirstCam;
    private readonly ViewCamera _outputWindowViewCamera = new();
    private readonly CameraInteraction _cameraInteraction = new();

    private double _lastUpdateTime;
    private bool _hasAnimationTimeChanged;

    private Guid _pickedCameraId = Guid.Empty;

    private Instance? _drawnInstance;
    private readonly List<ICamera> _recentlyUsedCameras = [];

    private ControlModes _lastControlMode;

    private ICamera? _firstCamInGraph;
}