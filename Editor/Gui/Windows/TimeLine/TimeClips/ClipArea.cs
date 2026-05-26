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
/// renders both op-backed <see cref="TimeClip"/>s and symbol-level
/// <see cref="T3.Core.Audio.TimelineAudioClip"/>s, dispatches keyboard actions, and
/// implements the public <see cref="ITimeObjectManipulation"/> /
/// <see cref="IValueSnapAttractor"/> interfaces consumed by <see cref="TimeLineCanvas"/>.
///
/// Most of the interaction work is delegated to two siblings:
/// <list type="bullet">
/// <item><see cref="TimeClipInteractions"/> — op-backed clip selection, drag command lifecycle,
///       delete-with-children, split-at-time, snap.</item>
/// <item><see cref="AudioClipInteractions"/> — audio clip selection, file drop, delete.
///       Per-clip drag handling lives directly in <see cref="TimelineAudioClipItem"/>.</item>
/// </list>
/// </summary>
internal sealed class ClipArea : ITimeObjectManipulation, IValueSnapAttractor
{
    public ClipArea(TimeLineCanvas timeLineCanvas,
                    Func<Instance> getCompositionOp,
                    Func<Guid, bool> requestChildCompositionFunc,
                    ValueSnapHandler snapHandlerForU)
    {
        _getCompositionOp = getCompositionOp;
        _context = new LayerContext(new ClipSelection(timeLineCanvas.NodeSelection),
                                    requestChildCompositionFunc,
                                    timeLineCanvas,
                                    snapHandlerForU);
        OpClips = new TimeClipInteractions(_context, getCompositionOp);
        AudioClips = new AudioClipInteractions(_context, getCompositionOp);

        // Cross-references so each helper can drive the other during cross-type drag
        // (drag-op-clip also moves audio clips in audio selection, and vice versa).
        OpClips.AudioInteractions = AudioClips;
        AudioClips.OpInteractions = OpClips;
    }

    /// <summary>
    /// Per-frame shared state passed into <see cref="TimeClipItem"/> and the interaction
    /// helpers. <see cref="ClipSelection"/> is op-clip-only — audio clips use the parallel
    /// selection set on <see cref="AudioClipInteractions"/>.
    /// </summary>
    internal sealed record LayerContext(
        ClipSelection ClipSelection,
        Func<Guid, bool> RequestChildComposition,
        TimeLineCanvas TimeCanvas,
        ValueSnapHandler SnapHandler);

    public TimeClipInteractions OpClips { get; }
    public AudioClipInteractions AudioClips { get; }

    public void Draw(Instance compositionOp, Playback playback, ValueSnapHandler snapHandler)
    {
        _drawList = ImGui.GetWindowDrawList();
        _playback = playback;
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

        AudioClips.HandleFileDrop(compositionOp, _minScreenPos, _minLayerIndex, _playback);

        // Layer-area height drag handle. Only meaningful when there is at least one op clip
        // to size; audio-clip-only compositions still draw rows but don't trigger this.
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
            AudioClips.DeleteSelectedClips(compositionOp);
        }
    }

    private void DrawAllLayers(Instance compositionOp)
    {
        var opClips = _context.ClipSelection.CompositionTimeClips.Values;
        var audioClips = compositionOp.Symbol.CompositionSettings.Playback.AudioClips;

        var visibleAudioClipCount = 0;
        foreach (var ac in audioClips)
        {
            if (ac.IsMainSoundtrack || string.IsNullOrEmpty(ac.AssetPath))
                continue;
            visibleAudioClipCount++;
        }

        if (opClips.Count == 0 && visibleAudioClipCount == 0)
        {
            LastHeight = 0;
            return;
        }

        // Layer bounds span both clip kinds so rows extend even when only one kind is present.
        _minLayerIndex = int.MaxValue;
        _maxLayerIndex = int.MinValue;
        foreach (var clip in opClips)
        {
            _minLayerIndex = Math.Min(clip.LayerIndex, _minLayerIndex);
            _maxLayerIndex = Math.Max(clip.LayerIndex, _maxLayerIndex);
        }
        foreach (var ac in audioClips)
        {
            if (ac.IsMainSoundtrack || string.IsNullOrEmpty(ac.AssetPath))
                continue;
            _minLayerIndex = Math.Min(ac.LayerIndex, _minLayerIndex);
            _maxLayerIndex = Math.Max(ac.LayerIndex, _maxLayerIndex);
        }

        var min = ImGui.GetCursorScreenPos() + new Vector2(0, LayerHeight * 0.5f);
        var max = min + new Vector2(ImGui.GetContentRegionAvail().X,
                                    LayerHeight * (_maxLayerIndex - _minLayerIndex + 1) + 1);
        LastHeight = max.Y - min.Y + 5;
        _drawList.AddRectFilled(new Vector2(min.X, max.Y - 2),
                                new Vector2(max.X, max.Y), UiColors.GridLines.Fade(0.6f));

        var layerRect = new ImRect(min, max);
        OpClips.DrawClips(compositionOp, layerRect, _minLayerIndex, _drawList);

        if (visibleAudioClipCount > 0)
            AudioClips.DrawClips(compositionOp, layerRect, _minLayerIndex, _drawList);

        // Force the surrounding BeginGroup bbox to span the entire layer area so the
        // file-drop target (resolved via the group's "last item" rect) covers full width
        // and full height. Without this, the group's bbox shrinks to the union of the
        // emitted InvisibleButtons (clip bodies), and the right portion of the timeline
        // — past the last clip's edge — silently rejects audio file drops.
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
        AudioClips.UpdateSelectionForArea(screenArea, selectMode, _minScreenPos, _minLayerIndex);
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
        AudioClips.ClearSelection();
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
        AudioClips.CheckForSnap(ref snapResult);
    }

    // ---------------------------------------------------------------------------
    // Frame state
    // ---------------------------------------------------------------------------

    internal static float LayerHeight => UserSettings.Config.LayerHeight.Clamp(4, 40);

    private readonly LayerContext _context;
    private readonly Func<Instance> _getCompositionOp;

    private int _minLayerIndex = int.MaxValue;
    private int _maxLayerIndex = int.MinValue;
    private Vector2 _minScreenPos;
    private ImDrawListPtr _drawList;
    private Playback? _playback;

    private static float _layerHeightOnDragStart;
    private static float _mouseYOnDragStart;
}
