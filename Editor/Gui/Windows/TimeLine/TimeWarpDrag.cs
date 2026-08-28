#nullable enable
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Piecewise-linear time-warp drag engine. Powers three interactions:
/// <list type="bullet">
/// <item>Dragging an inner TimeWarp handle (two-segment remap around it).</item>
/// <item>Dragging an SRI edge when inner handles exist (single-segment remap against the nearest handle).</item>
/// <item>Middle-translate when handles exist (pure translation of keys, clips, and handles).</item>
/// </list>
/// Builds a <see cref="MacroCommand"/> on completion containing the keyframe change,
/// the clip-move, and optionally a <see cref="SetTimeWarpHandlesCommand"/> so undo/redo
/// restores handle positions along with the retimed elements.
/// </summary>
internal sealed class TimeWarpDrag
{
    public TimeWarpDrag(TimeLineCanvas canvas, TimeWarpHandleSet handles)
    {
        _canvas = canvas;
        _handles = handles;
    }

    public bool IsActive { get; private set; }

    /// <summary>Start a drag. <paramref name="origHandleU"/> is the moving anchor; fixed boundaries pin the segment ends.</summary>
    public void Begin(Instance composition,
                      double origHandleU,
                      double prevBoundary,
                      double nextBoundary,
                      bool singleSegment,
                      double segmentLeftU,
                      double segmentRightU,
                      bool pureTranslation,
                      bool trackHandlePositions,
                      bool useAllKeyframes)
    {
        Reset();
        _origHandleU = origHandleU;
        _prev = prevBoundary;
        _next = nextBoundary;
        _singleSegment = singleSegment;
        _segLeftU = segmentLeftU;
        _segRightU = segmentRightU;
        _pureTranslation = pureTranslation;

        if (trackHandlePositions)
        {
            _handles.CopyTo(_origHandles);
            _handlesCommand = new SetTimeWarpHandlesCommand(_handles, _origHandles);
        }

        // Snapshot keyframes that are in the affected range (or all, for pure translation),
        // aggregated across every active keyframe editor (DopeSheet + CurveEditor when split).
        // The warp geometry (handles, boundaries) is in playback time; keys live in their row's
        // curve time — snapshot each key's playback position and write back through its mapping.
        var editors = _canvas.KeyframeEditors;
        foreach (var (def, mapping) in editors.EnumerateKeyframesWithMapping(selectedOnly: !useAllKeyframes))
        {
            var globalU = mapping.ToGlobal(def.U);
            if (!pureTranslation && !InsideAffectedRange(globalU))
                continue;
            _keys.Add(def);
            _keyOrigU.Add(globalU);
            _keyMappings.Add(mapping);
        }

        if (_keys.Count > 0)
        {
            editors.CopyAllCurvesTo(_curves);
            _keyframesCommand = new ChangeKeyframesCommand(_keys, _curves);
        }

        // Clips: retime start/end of each selected clip.
        foreach (var clip in _canvas.ClipArea.EnumerateSelectedClips())
        {
            _clips.Add(clip);
            _clipOrigStart.Add(clip.TimeRange.Start);
            _clipOrigEnd.Add(clip.TimeRange.End);
        }

        if (_clips.Count > 0)
        {
            _clipBuffer.Clear();
            for (var i = 0; i < _clips.Count; i++)
                _clipBuffer.Add(_clips[i]);
            _clipsCommand = new MoveTimeClipsCommand(composition, _clipBuffer);
        }

        IsActive = true;
    }

    /// <summary>Apply this frame's remap. <paramref name="currentU"/> is the moving anchor's new U.</summary>
    public void Update(double currentU)
    {
        if (!IsActive)
            return;

        if (_pureTranslation)
        {
            var du = currentU - _origHandleU;
            for (var i = 0; i < _keys.Count; i++)
                _keys[i].U = _keyMappings[i].ToLocal(_keyOrigU[i] + du);

            for (var i = 0; i < _clips.Count; i++)
            {
                var clip = _clips[i];
                clip.TimeRange.Start = _clipOrigStart[i] + (float)du;
                clip.TimeRange.End = _clipOrigEnd[i] + (float)du;
            }

            if (_handlesCommand != null)
            {
                for (var i = 0; i < _origHandles.Count && i < _handles.Count; i++)
                    _handles.SetValueUnsorted(i, _origHandles[i] + du);
            }

            AnimationParameterEditing.CurvesTablesNeedsRefresh = true;
            return;
        }

        for (var i = 0; i < _keys.Count; i++)
            _keys[i].U = _keyMappings[i].ToLocal(RemapPiecewise(_keyOrigU[i], currentU));

        for (var i = 0; i < _clips.Count; i++)
        {
            var clip = _clips[i];
            var newStart = (float)RemapPiecewise(_clipOrigStart[i], currentU);
            var newEnd = (float)RemapPiecewise(_clipOrigEnd[i], currentU);
            if (newEnd - newStart < 1 / 60f)
                newEnd = newStart + 1 / 60f;
            clip.TimeRange.Start = newStart;
            clip.TimeRange.End = newEnd;
        }

        AnimationParameterEditing.CurvesTablesNeedsRefresh = true;
    }

    /// <summary>Finalize and push the macro command onto the undo stack.</summary>
    public void Complete()
    {
        if (!IsActive)
            return;

        if (_keys.Count == 0 && _clips.Count == 0 && _handlesCommand == null)
        {
            Reset();
            return;
        }

        var commands = new List<ICommand>(3);
        if (_keyframesCommand != null)
        {
            _keyframesCommand.StoreCurrentValues();
            commands.Add(_keyframesCommand);
        }
        if (_clipsCommand != null)
        {
            _clipsCommand.StoreCurrentValues();
            commands.Add(_clipsCommand);
        }
        if (_handlesCommand != null)
        {
            _handlesCommand.StoreCurrentValues(_handles);
            commands.Add(_handlesCommand);
        }

        if (commands.Count > 0)
        {
            var macro = new MacroCommand("TimeWarp", commands);
            UndoRedoStack.AddAndExecute(macro);
        }

        Reset();
    }

    private bool InsideAffectedRange(double u)
    {
        if (_singleSegment)
            return u >= _segLeftU && u <= _segRightU;
        return u >= _prev && u <= _next;
    }

    private double RemapPiecewise(double t, double newU)
    {
        var origU = _origHandleU;
        if (_singleSegment)
        {
            // One-segment warp: either segLeft or segRight coincides with origU (the moving point).
            if (t < _segLeftU || t > _segRightU) return t;
            if (Math.Abs(_segLeftU - origU) < 1e-9)
            {
                // Left end is moving; right end fixed.
                var right = _segRightU;
                var lenOrig = right - origU;
                if (Math.Abs(lenOrig) < 1e-9) return t;
                var ratio = (t - origU) / lenOrig;
                return newU + ratio * (right - newU);
            }
            else
            {
                // Right end is moving; left end fixed.
                var left = _segLeftU;
                var lenOrig = origU - left;
                if (Math.Abs(lenOrig) < 1e-9) return t;
                var ratio = (t - left) / lenOrig;
                return left + ratio * (newU - left);
            }
        }

        // Two-segment warp: (prev, origU) and (origU, next).
        if (t < _prev || t > _next) return t;
        if (t <= origU)
        {
            var lenOrig = origU - _prev;
            if (Math.Abs(lenOrig) < 1e-9) return t;
            var ratio = (t - _prev) / lenOrig;
            return _prev + ratio * (newU - _prev);
        }
        else
        {
            var lenOrig = _next - origU;
            if (Math.Abs(lenOrig) < 1e-9) return t;
            var ratio = (t - origU) / lenOrig;
            return newU + ratio * (_next - newU);
        }
    }

    private void Reset()
    {
        _keys.Clear();
        _keyOrigU.Clear();
        _keyMappings.Clear();
        _clips.Clear();
        _clipOrigStart.Clear();
        _clipOrigEnd.Clear();
        _origHandles.Clear();
        _keyframesCommand = null;
        _clipsCommand = null;
        _handlesCommand = null;
        _pureTranslation = false;
        _singleSegment = false;
        IsActive = false;
    }

    private readonly TimeLineCanvas _canvas;
    private readonly TimeWarpHandleSet _handles;

    private double _origHandleU;
    private double _prev;
    private double _next;
    private bool _singleSegment;
    private double _segLeftU;
    private double _segRightU;
    private bool _pureTranslation;

    private readonly List<VDefinition> _keys = new(64);
    private readonly List<double> _keyOrigU = new(64); // playback-time positions at drag start
    private readonly List<TimeLineCanvas.ParamTimeMapping> _keyMappings = new(64);
    private readonly List<TimeClip> _clips = new(16);
    private readonly List<float> _clipOrigStart = new(16);
    private readonly List<float> _clipOrigEnd = new(16);
    private readonly List<double> _origHandles = new(8);
    private readonly List<Curve> _curves = new(32);
    private readonly List<TimeClip> _clipBuffer = new(16);

    private ChangeKeyframesCommand? _keyframesCommand;
    private MoveTimeClipsCommand? _clipsCommand;
    private SetTimeWarpHandlesCommand? _handlesCommand;
}
