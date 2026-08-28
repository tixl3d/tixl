#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.UiHelpers;
using T3.Core.Operator;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// The KeySet strip: a thin band below the timeline ruler showing a baked icon for every keyframe
/// (or cluster of keyframes within sub-pixel distance) across the visible parameters.
///
/// Click: replace the keyframe selection with the clicked cluster's keys.
/// Drag:  move the cluster's keys through time without touching the selection.
///        The broader "move whole selection" interaction lives in <see cref="SelectionRangeIndicator"/>.
///
/// To make the drag survive bucket merges/splits (e.g. dragging a cluster past another),
/// hover + press is detected via a single strip-wide InvisibleButton with a stable ID
/// rather than per-bucket buttons. ImGui's active-item tracking therefore stays valid
/// for the full duration of the drag, regardless of how clustering changes underneath.
///
/// The bucket layout itself is cached and only rebuilt when the view (scale/scroll), the
/// animation parameters, curve contents, or the keyframe selection change.
/// </summary>
internal sealed class KeySetStrip
{
    public KeySetStrip(TimeLineCanvas canvas)
    {
        _canvas = canvas;
    }

    public void Draw(
        Instance composition,
        List<TimeLineCanvas.AnimationParameter> animationParameters,
        KeyframeEditorGroup editors,
        ImDrawListPtr drawList)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var scaleX = _canvas.Scale.X;
        var scrollX = _canvas.Scroll.X;

        var hash = ComputeStateHash(animationParameters, editors, scaleX, scrollX, windowPos, windowSize);
        if (hash != _cachedStateHash)
        {
            RebuildBuckets(animationParameters, editors, windowPos, windowSize);
            _cachedStateHash = hash;
        }

        var scale = T3Ui.UiScaleFactor;
        var iconSize = 7f * scale;
        var iconY = windowPos.Y + (windowSize.Y - iconSize) * 0.5f;

        // A single strip-wide InvisibleButton claims the mouse interaction for the whole SA.
        // Stable ID means ImGui's active-item tracking persists across bucket rebuilds, so a
        // drag that crosses another cluster's position doesn't get cancelled.
        ImGui.SetCursorScreenPos(windowPos);
        ImGui.InvisibleButton("##keySetStrip", windowSize);

        var isItemHovered = ImGui.IsItemHovered();
        var isItemActive = ImGui.IsItemActive();
        var isItemActivated = ImGui.IsItemActivated();
        var isItemDeactivated = ImGui.IsItemDeactivated();

        // Locate bucket under mouse (for hover + press bucket identification).
        var hoveredIndex = -1;
        if ((isItemHovered || isItemActive) && _cachedBuckets.Count > 0)
        {
            var mouseX = ImGui.GetMousePos().X;
            var hitThreshold = iconSize * 0.5f + 1 * scale;
            var bestDist = float.MaxValue;
            for (var i = 0; i < _cachedBuckets.Count; i++)
            {
                var d = MathF.Abs(_cachedBuckets[i].CenterX - mouseX);
                if (d < hitThreshold && d < bestDist)
                {
                    bestDist = d;
                    hoveredIndex = i;
                }
            }
        }

        if (isItemHovered || isItemActive)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (isItemActivated)
        {
            if (hoveredIndex >= 0)
            {
                _pressedBucketStableId = _cachedBuckets[hoveredIndex].StableId;
                _isPressed = true;
            }
            else
            {
                // Pressed on SA background: click = clear selection; drag = fence-select buckets.
                _isBackgroundPressed = true;
                _fenceStartScreen = ImGui.GetMousePos();
                // Snapshot the pre-fence selection so Add/Remove modes are relative to it every frame,
                // not accumulated across frames as the fence moves.
                editors.CopyAllSelectedKeyframesTo(_preFenceSnapshot);
            }
        }

        if (_isPressed && isItemActive && !_isDragging
            && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f * scale))
        {
            var pressedIndex = FindBucketByStableId(_pressedBucketStableId);
            if (pressedIndex >= 0)
            {
                CaptureDragSet(pressedIndex);
                _dragCommand = new ChangeKeyframesCommand(_draggingKeys, _draggingCurves);
                // The anchor key (first in _draggingKeys) tracks with the mouse using this fixed grab offset.
                // Snapping targets the anchor's would-be U, matching the dope sheet's behavior.
                var mouseU = _canvas.InverseTransformX(ImGui.GetMousePos().X);
                _dragGrabOffset = mouseU - _draggingMappings[0].ToGlobal(_draggingKeys[0].U);
                _isDragging = true;
                // Tell DSA's snap attractor to skip the bucket's own keys for this drag — the
                // bucket carries the whole set, not just the selected ones, so unselected siblings
                // would otherwise pull the dragged anchor onto themselves each frame.
                _canvas.DopeSheetArea.ExternalDragSnapExclusions = _draggingKeys;
            }
        }

        // Background fence: activate once the user actually drags past the click threshold.
        if (_isBackgroundPressed && isItemActive && !_isFencing
            && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f * scale))
        {
            _isFencing = true;
        }

        var liveFenceRect = default(ImRect);
        var liveFenceMode = SelectionFence.SelectModes.Replace;
        if (_isFencing && isItemActive)
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            liveFenceRect = BuildFenceRect(ImGui.GetMousePos(), windowPos, windowSize);
            liveFenceMode = GetSelectMode(ImGui.GetIO());
            drawList.AddRectFilled(liveFenceRect.Min, liveFenceRect.Max, UiColors.Selection.Fade(0.15f));
            drawList.AddRect(liveFenceRect.Min, liveFenceRect.Max, UiColors.Selection.Fade(0.5f));

            // Apply the fence live (from the pre-fence snapshot) so the DopeSheet keyframes reflect
            // the selection while dragging — not just after release. Add/Remove modes stay coherent
            // because we rebase from the snapshot each frame instead of accumulating.
            editors.ReplaceKeyframeSelection(_preFenceSnapshot);
            ApplyFenceSelection(liveFenceRect, liveFenceMode, editors);
        }

        if (_isDragging && isItemActive && _draggingKeys.Count > 0)
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;

            var mouseU = _canvas.InverseTransformX(ImGui.GetMousePos().X);
            var desiredAnchorU = mouseU - _dragGrabOffset;
            if (!ImGui.GetIO().KeyShift
                && _canvas.SnapHandlerForU.TryCheckForSnapping(desiredAnchorU, out var snappedValue, _canvas.Scale.X,
                                                               _canvas.SelectionDragSnapExclusions))
            {
                desiredAnchorU = (float)snappedValue;
            }

            // du is a playback-time delta; each key scales it into its own curve time.
            var du = desiredAnchorU - _draggingMappings[0].ToGlobal(_draggingKeys[0].U);
            if (du != 0)
                editors.ApplyKeyframePlaybackTimeOffset(_draggingKeys, _draggingMappings, du);
        }

        if (isItemDeactivated)
        {
            if (_isDragging && _dragCommand != null)
            {
                _dragCommand.StoreCurrentValues();
                UndoRedoStack.AddAndExecute(_dragCommand);
                _dragCommand = null;
                _isDragging = false;
            }
            else if (_isPressed)
            {
                // Click without drag: replace the keyframe selection with this bucket's keys.
                var pressedIndex = FindBucketByStableId(_pressedBucketStableId);
                if (pressedIndex >= 0)
                {
                    _tempBucketKeys.Clear();
                    var b = _cachedBuckets[pressedIndex];
                    for (var i = 0; i < b.KeyCount; i++)
                        _tempBucketKeys.Add(_rawKeys[b.FirstKeyIndex + i].Def);
                    editors.ReplaceKeyframeSelection(_tempBucketKeys);
                }
            }
            else if (_isFencing)
            {
                // Re-apply using final mouse position (the live-apply branch is gated on isItemActive
                // which is false on the deactivation frame, so the committed state could otherwise lag
                // the user's last motion by one frame).
                var fenceRect = BuildFenceRect(ImGui.GetMousePos(), windowPos, windowSize);
                editors.ReplaceKeyframeSelection(_preFenceSnapshot);
                ApplyFenceSelection(fenceRect, GetSelectMode(ImGui.GetIO()), editors);
            }
            else if (_isBackgroundPressed)
            {
                // Click on SA background without drag: clear keyframe selection.
                _tempBucketKeys.Clear();
                editors.ReplaceKeyframeSelection(_tempBucketKeys);
            }

            _isPressed = false;
            _isBackgroundPressed = false;
            _isFencing = false;
            _canvas.DopeSheetArea.ExternalDragSnapExclusions = null;
            _draggingKeys.Clear();
            _draggingCurves.Clear();
            _draggingMappings.Clear();
        }

        // Icon render
        if (_cachedBuckets.Count == 0)
            return;

        var highlightedIndex = _isDragging
                                   ? FindBucketByStableId(_pressedBucketStableId)
                                   : hoveredIndex;

        var defaultColor = UiColors.ForegroundFull.Fade(0.7f);
        var hoverColor = UiColors.ForegroundFull;

        for (var i = 0; i < _cachedBuckets.Count; i++)
        {
            var b = _cachedBuckets[i];

            // While fencing, preview the effective selection so icons update before release.
            var effectiveSelected = b.Selected;
            if (_isFencing)
            {
                var inside = b.CenterX >= liveFenceRect.Min.X && b.CenterX <= liveFenceRect.Max.X;
                effectiveSelected = liveFenceMode switch
                                    {
                                        SelectionFence.SelectModes.Replace => inside ? b.Total : 0,
                                        SelectionFence.SelectModes.Add     => inside ? b.Total : b.Selected,
                                        SelectionFence.SelectModes.Remove  => inside ? 0 : b.Selected,
                                        _                                  => b.Selected,
                                    };
            }

            var icon = effectiveSelected == 0
                           ? Icon.KeyIndicator
                           : effectiveSelected < b.Total
                               ? Icon.KeyIndicatorSelectedPartially
                               : Icon.KeyIndicatorSelected;

            var pos = new Vector2(MathF.Floor(b.CenterX - iconSize * 0.5f) +1, MathF.Floor(iconY));
            var color = i == highlightedIndex ? hoverColor : defaultColor;
            Icons.DrawIconAtScreenPosition(icon, pos, drawList, color);
        }
    }

    private ImRect BuildFenceRect(Vector2 mousePos, Vector2 windowPos, Vector2 windowSize)
    {
        // Selection-area buckets sit on a single row, so the fence visual extends the full strip height.
        var minX = MathF.Min(_fenceStartScreen.X, mousePos.X);
        var maxX = MathF.Max(_fenceStartScreen.X, mousePos.X);
        return new ImRect(new Vector2(minX, windowPos.Y),
                          new Vector2(maxX, windowPos.Y + windowSize.Y));
    }

    private void ApplyFenceSelection(ImRect fenceRect, SelectionFence.SelectModes mode, KeyframeEditorGroup editors)
    {
        // Buckets are drawn on a single Y-row; fence selection is effectively a 1D X-range test.
        _tempBucketKeys.Clear();
        for (var i = 0; i < _cachedBuckets.Count; i++)
        {
            var b = _cachedBuckets[i];
            if (b.CenterX < fenceRect.Min.X || b.CenterX > fenceRect.Max.X)
                continue;

            for (var k = 0; k < b.KeyCount; k++)
                _tempBucketKeys.Add(_rawKeys[b.FirstKeyIndex + k].Def);
        }

        switch (mode)
        {
            case SelectionFence.SelectModes.Replace:
                editors.ReplaceKeyframeSelection(_tempBucketKeys);
                break;
            case SelectionFence.SelectModes.Add:
                editors.AddToKeyframeSelection(_tempBucketKeys);
                break;
            case SelectionFence.SelectModes.Remove:
                editors.RemoveFromKeyframeSelection(_tempBucketKeys);
                break;
        }
    }

    private static SelectionFence.SelectModes GetSelectMode(in ImGuiIOPtr io)
    {
        if (io.KeyShift) return SelectionFence.SelectModes.Add;
        if (io.KeyCtrl) return SelectionFence.SelectModes.Remove;
        return SelectionFence.SelectModes.Replace;
    }

    private int FindBucketByStableId(int stableId)
    {
        for (var i = 0; i < _cachedBuckets.Count; i++)
            if (_cachedBuckets[i].StableId == stableId)
                return i;

        // The original "first key" may now live inside a merged bucket — search raw keys instead.
        for (var i = 0; i < _rawKeys.Count; i++)
        {
            if (_rawKeys[i].Def.UniqueId != stableId)
                continue;
            for (var b = 0; b < _cachedBuckets.Count; b++)
            {
                var bucket = _cachedBuckets[b];
                if (i >= bucket.FirstKeyIndex && i < bucket.FirstKeyIndex + bucket.KeyCount)
                    return b;
            }
        }
        return -1;
    }

    private void CaptureDragSet(int bucketIndex)
    {
        _draggingKeys.Clear();
        _draggingCurves.Clear();
        _draggingMappings.Clear();
        var bucket = _cachedBuckets[bucketIndex];
        for (var i = 0; i < bucket.KeyCount; i++)
        {
            var rk = _rawKeys[bucket.FirstKeyIndex + i];
            _draggingKeys.Add(rk.Def);
            _draggingMappings.Add(rk.Mapping);
            var already = false;
            for (var c = 0; c < _draggingCurves.Count; c++)
            {
                if (_draggingCurves[c] == rk.Curve) { already = true; break; }
            }
            if (!already)
                _draggingCurves.Add(rk.Curve);
        }
    }

    private int ComputeStateHash(
        List<TimeLineCanvas.AnimationParameter> parameters,
        KeyframeEditorGroup editors,
        float scaleX,
        float scrollX,
        Vector2 windowPos,
        Vector2 windowSize)
    {
        var hash = 17;
        hash = hash * 31 + scaleX.GetHashCode();
        hash = hash * 31 + scrollX.GetHashCode();
        hash = hash * 31 + windowPos.X.GetHashCode();
        hash = hash * 31 + windowSize.X.GetHashCode();
        hash = hash * 31 + parameters.Count;
        for (var pi = 0; pi < parameters.Count; pi++)
        {
            var p = parameters[pi];
            hash = hash * 31 + p.Hash;
            for (var ci = 0; ci < p.Curves.Length; ci++)
                hash = hash * 31 + p.Curves[ci].ChangeCount;

            // Dragging an enclosing time clip moves keys' playback positions without touching the
            // curves — the mapping must contribute, or the strip shows stale dot positions.
            var mapping = p.BuildTimeMapping();
            hash = hash * 31 + mapping.GlobalAtLocalZero.GetHashCode();
            hash = hash * 31 + mapping.Rate.GetHashCode();
        }
        hash = hash * 31 + editors.AggregateSelectionChangeCounter;
        return hash;
    }

    private void RebuildBuckets(
        List<TimeLineCanvas.AnimationParameter> animationParameters,
        KeyframeEditorGroup editors,
        Vector2 windowPos,
        Vector2 windowSize)
    {
        _cachedBuckets.Clear();
        _rawKeys.Clear();

        var minScreenX = windowPos.X;
        var maxScreenX = windowPos.X + windowSize.X;
        var cullPad = 8 * T3Ui.UiScaleFactor;

        for (var pi = 0; pi < animationParameters.Count; pi++)
        {
            var param = animationParameters[pi];
            // Keys live in the parameter's curve time; the strip (like the ruler) is in playback time.
            var mapping = param.BuildTimeMapping();
            for (var ci = 0; ci < param.Curves.Length; ci++)
            {
                var curve = param.Curves[ci];
                var defs = curve.GetVDefinitions();
                for (var ki = 0; ki < defs.Count; ki++)
                {
                    var def = defs[ki];
                    var x = _canvas.TransformX((float)mapping.ToGlobal(def.U));
                    if (x < minScreenX - cullPad || x > maxScreenX + cullPad)
                        continue;

                    _rawKeys.Add(new RawKey
                                     {
                                         ScreenX = x,
                                         IsSelected = editors.IsKeyframeSelectedInAny(def),
                                         Def = def,
                                         Curve = curve,
                                         Mapping = mapping,
                                     });
                }
            }
        }

        if (_rawKeys.Count == 0)
            return;

        _rawKeys.Sort(_compareByX);

        var mergeThreshold = 2f * T3Ui.UiScaleFactor;

        var runStartIndex = 0;
        var runStartX = _rawKeys[0].ScreenX;
        var runEndX = runStartX;
        var runTotal = 0;
        var runSelected = 0;

        for (var i = 0; i < _rawKeys.Count; i++)
        {
            var k = _rawKeys[i];
            var wouldExtend = k.ScreenX - runStartX <= mergeThreshold;

            if (i == 0 || wouldExtend)
            {
                if (i == 0)
                {
                    runStartIndex = 0;
                    runStartX = k.ScreenX;
                }
                runEndX = k.ScreenX;
                runTotal++;
                if (k.IsSelected) runSelected++;
                continue;
            }

            EmitBucket(runStartIndex, runStartX, runEndX, runTotal, runSelected);

            runStartIndex = i;
            runStartX = k.ScreenX;
            runEndX = k.ScreenX;
            runTotal = 1;
            runSelected = k.IsSelected ? 1 : 0;
        }

        EmitBucket(runStartIndex, runStartX, runEndX, runTotal, runSelected);
    }

    private void EmitBucket(int firstIndex, float startX, float endX, int total, int selected)
    {
        _cachedBuckets.Add(new Bucket
                               {
                                   CenterX = (startX + endX) * 0.5f,
                                   Total = total,
                                   Selected = selected,
                                   FirstKeyIndex = firstIndex,
                                   KeyCount = total,
                                   StableId = _rawKeys[firstIndex].Def.UniqueId,
                               });
    }

    private struct RawKey
    {
        public float ScreenX;
        public bool IsSelected;
        public VDefinition Def;
        public Curve Curve;
        public TimeLineCanvas.ParamTimeMapping Mapping;
    }

    private struct Bucket
    {
        public float CenterX;
        public int Total;
        public int Selected;
        public int FirstKeyIndex;
        public int KeyCount;
        public int StableId;
    }

    private static readonly Comparison<RawKey> _compareByX =
        static (a, b) => a.ScreenX.CompareTo(b.ScreenX);

    private readonly List<RawKey> _rawKeys = new(256);
    private readonly List<Bucket> _cachedBuckets = new(256);
    private readonly List<VDefinition> _tempBucketKeys = new(32);
    private readonly List<VDefinition> _preFenceSnapshot = new(64);
    private readonly List<VDefinition> _draggingKeys = new(32);
    private readonly List<Curve> _draggingCurves = new(8);
    private readonly List<TimeLineCanvas.ParamTimeMapping> _draggingMappings = new(32);
    private int _cachedStateHash;
    private bool _isPressed;
    private bool _isDragging;
    private bool _isBackgroundPressed;
    private bool _isFencing;
    private Vector2 _fenceStartScreen;
    private int _pressedBucketStableId;
    private double _dragGrabOffset;
    private ChangeKeyframesCommand? _dragCommand;
    private readonly TimeLineCanvas _canvas;
}
