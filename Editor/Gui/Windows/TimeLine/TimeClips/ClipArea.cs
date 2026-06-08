#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// The clip-editing area inside the timeline window. Orchestrates per-frame layout,
/// renders op-backed <see cref="TimeClip"/>s (including <c>[AudioClip]</c>, which is itself a
/// TimeClip op), dispatches keyboard actions, and implements the public
/// <see cref="ITimeObjectManipulation"/> / <see cref="IValueSnapAttractor"/> interfaces
/// consumed by <see cref="TimeLineCanvas"/>. Interaction work is delegated to
/// <see cref="TimeClipInteractions"/> (selection, drag lifecycle, delete-with-children,
/// split-at-time, snap).
/// </summary>
internal sealed class ClipArea : ITimeObjectManipulation, IValueSnapAttractor
{
    public ClipArea(TimeLineCanvas timeLineCanvas,
                    Func<Instance> getCompositionOp,
                    Func<Guid, bool> requestChildCompositionFunc,
                    ValueSnapHandler snapHandlerForU)
    {
        _context = new LayerContext(new ClipSelection(timeLineCanvas.NodeSelection),
                                    requestChildCompositionFunc,
                                    timeLineCanvas,
                                    snapHandlerForU);
        OpClips = new TimeClipInteractions(_context, getCompositionOp);
    }

    /// <summary>
    /// Per-frame shared state passed into <see cref="TimeClipItem"/> and the interaction helpers.
    /// </summary>
    internal sealed record LayerContext(
        ClipSelection ClipSelection,
        Func<Guid, bool> RequestChildComposition,
        TimeLineCanvas TimeCanvas,
        ValueSnapHandler SnapHandler);

    public TimeClipInteractions OpClips { get; }

    public void Draw(Instance compositionOp, Playback playback, ValueSnapHandler snapHandler)
    {
        _drawList = ImGui.GetWindowDrawList();
        OpClips.SetPlayback(playback);

        ImGui.BeginGroup();
        {
            _context.ClipSelection.UpdateForComposition(compositionOp);
            _minScreenPos = ImGui.GetCursorScreenPos();

            DrawAllLayers(compositionOp);
            OpClips.DrawContextMenuItems(compositionOp);
            HandleKeyboardActions(compositionOp);
            ClipTimingEditor.DrawPopUp(_context);
            if (_context.ClipSelection.AllClipIds.Count > 0)
                FormInputs.AddVerticalSpace(1);
        }
        ImGui.EndGroup();

        // Audio files dropped onto the clip area become [AudioClip] ops at the drop time / layer.
        AudioClipTimelineDrop.Handle(compositionOp, _context.TimeCanvas, _minScreenPos.Y, _minLayerIndex);

        // Layer-area height drag handle. Only meaningful when there is at least one op clip to size.
        if (_context.ClipSelection.AllClipIds.Count > 0)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2);
            ImGui.Button("##layerHeight", new Vector2(20, 3) * T3Ui.UiScaleFactor);
            if (ImGui.IsItemActivated())
            {
                _layerHeightOnDragStart = UserSettings.Config.LayerHeight;
                _mouseYOnDragStart = ImGui.GetMousePos().Y;
            }
            if (ImGui.IsItemActive())
            {
                var layerCount = (_maxLayerIndex - _minLayerIndex + 1).Clamp(1, 999999);
                var mouseDeltaY = ImGui.GetMousePos().Y - _mouseYOnDragStart;
                var heightDelta = mouseDeltaY / layerCount;
                UserSettings.Config.LayerHeight = (_layerHeightOnDragStart + heightDelta).Clamp(4, 50);
            }
        }
    }

    private void HandleKeyboardActions(Instance compositionOp)
    {
        if (UserActions.SplitSelectedOrHoveredClips.Triggered())
            OpClips.SplitClipsAtTime(compositionOp);

        if (UserActions.DeleteSelection.Triggered())
        {
            OpClips.DeleteSelectedClips(compositionOp);
        }
    }

    private void DrawAllLayers(Instance compositionOp)
    {
        var opClips = _context.ClipSelection.CompositionTimeClips.Values;
        if (opClips.Count == 0)
        {
            LastHeight = 0;
            return;
        }

        _minLayerIndex = int.MaxValue;
        _maxLayerIndex = int.MinValue;
        foreach (var clip in opClips)
        {
            _minLayerIndex = Math.Min(clip.LayerIndex, _minLayerIndex);
            _maxLayerIndex = Math.Max(clip.LayerIndex, _maxLayerIndex);
        }

        var min = ImGui.GetCursorScreenPos() + new Vector2(0, LayerHeight * 0.5f);
        var max = min + new Vector2(ImGui.GetContentRegionAvail().X,
                                    LayerHeight * (_maxLayerIndex - _minLayerIndex + 1) + 1);
        LastHeight = max.Y - min.Y + 5;
        _drawList.AddRectFilled(new Vector2(min.X, max.Y - 2),
                                new Vector2(max.X, max.Y), UiColors.GridLines.Fade(0.6f));

        var layerRect = new ImRect(min, max);
        OpClips.DrawClips(compositionOp, layerRect, _minLayerIndex, _drawList);

        // Span the surrounding BeginGroup bbox over the whole layer area so the layer-height
        // drag handle and hit-testing cover the full width/height, not just the union of the
        // emitted clip-body buttons.
        ImGui.SetCursorScreenPos(min);
        ImGui.Dummy(max - min);

        ImGui.SetCursorScreenPos(min + new Vector2(0, LayerHeight));
    }

    // ---------------------------------------------------------------------------
    // External API (consumed by TimeLineCanvas, TimeWarpDrag, etc.)
    // ---------------------------------------------------------------------------

    public float LastHeight { get; private set; }

    public void UpdateSelectionForArea(ImRect screenArea, SelectionFence.SelectModes selectMode)
    {
        OpClips.UpdateSelectionForArea(screenArea, selectMode, _minScreenPos, _minLayerIndex);
    }

    public IEnumerable<TimeClip> EnumerateSelectedClips() => OpClips.EnumerateSelectedClips();
    public bool TryGetBounds(out ImRect bounds, bool useAllIfNonSelected = true)
        => OpClips.TryGetBounds(out bounds, useAllIfNonSelected);
    public bool HasSelectedClips => OpClips.HasSelectedClips;
    public bool HasAnyClips => OpClips.HasAnyClips;
    public void SelectAllClips() => OpClips.SelectAllClips();
    public TimeRange GetSelectionTimeRange() => OpClips.GetSelectionTimeRange();
    public TimeRange GetAllClipsTimeRange() => OpClips.GetAllClipsTimeRange();

    // ---------------------------------------------------------------------------
    // ITimeObjectManipulation
    // ---------------------------------------------------------------------------

    public void ClearSelection()
    {
        OpClips.ClearSelection();
    }

    public ICommand StartDragCommand(in Guid compositionSymbolId) => OpClips.StartDragCommand();

    void ITimeObjectManipulation.UpdateDragCommand(double dt, double dy) => OpClips.UpdateDragCommand(dt, dy);

    public void UpdateDragAtStartPointCommand(double dt, double dv) => OpClips.UpdateDragAtStartPointCommand(dt, dv);

    public void UpdateDragAtEndPointCommand(double dt, double dv) => OpClips.UpdateDragAtEndPointCommand(dt, dv);

    void ITimeObjectManipulation.UpdateDragStretchCommand(double scaleU, double scaleV, double originU, double originV)
        => OpClips.UpdateDragStretchCommand(scaleU, scaleV, originU, originV);

    void ITimeObjectManipulation.CompleteDragCommand() => OpClips.CompleteDragCommand();

    void ITimeObjectManipulation.DeleteSelectedElements(Instance instance)
    {
        //TODO: Implement deleting of layers with delete key
    }

    // ---------------------------------------------------------------------------
    // IValueSnapAttractor
    // ---------------------------------------------------------------------------

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        OpClips.CheckForSnap(ref snapResult);
    }

    // ---------------------------------------------------------------------------
    // Frame state
    // ---------------------------------------------------------------------------

    internal static float LayerHeight => UserSettings.Config.LayerHeight.Clamp(4, 40);

    private readonly LayerContext _context;

    private int _minLayerIndex = int.MaxValue;
    private int _maxLayerIndex = int.MinValue;
    private Vector2 _minScreenPos;
    private ImDrawListPtr _drawList;

    private static float _layerHeightOnDragStart;
    private static float _mouseYOnDragStart;
}
