#nullable enable
using ImGuiNET;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.MagGraph.Interaction;

/*
 * Tracks a cut or reroute gesture against the actual tessellated paths drawn by the canvas.
 * Mouse segments, hit tolerance, and the bounded trail use screen coordinates; stored hits and
 * anchor positions use canvas coordinates. Occurrences are collected once and committed on release.
 * _compositionId and _version invalidate the gesture if its graph changes, while reusable buffers
 * avoid rebuilding placement data on every frame when no additional wire has been crossed.
 */
internal sealed class ConnectionStroke
{
    internal ConnectionStroke()
    {
        ObservePath = TestDrawnPath;
    }

    // Invoke after SetConnection and before PathStroke clears the draw list's path; the delegate is reused.
    internal Action<ImDrawListPtr> ObservePath { get; }
    internal bool IsActive { get; private set; }
    internal bool IsCut { get; private set; }

    internal void Begin(GraphUiContext context, bool cut, Vector2 position)
    {
        Cancel();
        _context = context;
        _compositionId = context.CompositionInstance.Symbol.Id;
        _version = context.CompositionInstance.Symbol.VersionCounter;
        IsActive = true;
        IsCut = cut;
        _start = _previous = _current = position;
        var capacity = context.Layout.MagConnections.Count;
        _hits.EnsureCapacity(capacity);
        _hitSet.EnsureCapacity(capacity);
        _anchorPositions.EnsureCapacity(capacity);
        _placementBuffer.EnsureCapacity(capacity);
        AddTrailPoint(position);
    }

    internal bool IsCurrent(GraphUiContext context)
    {
        return IsActive
               && ReferenceEquals(_context, context)
               && context.CompositionInstance.Symbol.Id == _compositionId
               && context.CompositionInstance.Symbol.VersionCounter == _version
               && !context.CompositionInstance.Symbol.SymbolPackage.IsReadOnly;
    }

    internal void UpdatePosition(Vector2 position, float dragThreshold)
    {
        _previous = _current;
        _current = position;
        if (!_dragging && Vector2.DistanceSquared(_start, position) >= dragThreshold * dragThreshold)
        {
            _dragging = true;
            _previous = _start;
        }

        _hasSegment = _dragging && _previous != _current;
        if (_hasSegment)
            AddTrailPoint(position);
    }

    internal void SetConnection(MagGraphConnection? connection)
    {
        _connection = connection;
    }

    // Snapped wires have a visible marker instead of a cable path to intersect.
    internal void TestMarker(ImDrawListPtr drawList, Vector2 center, float radius)
    {
        if (!IsActive || _connection == null)
            return;

        var clipMin = drawList.GetClipRectMin();
        var clipMax = drawList.GetClipRectMax();
        var visibleCenter = Vector2.Clamp(center, clipMin, clipMax);
        if (Vector2.DistanceSquared(center, visibleCenter) > radius * radius)
            return;

        var start = _previous;
        var end = _current;
        if (_hasSegment && ClipSegment(ref start, ref end, clipMin, clipMax)
                        && TryIntersectSegments(start, end, center, center,
                                                radius + HitTolerance, out _))
        {
            AddHit(visibleCenter);
        }
    }

    internal bool Contains(MagGraphConnection connection)
    {
        return IsActive && _hitSet.Contains(RerouteOperations.Capture(connection));
    }

    internal void DrawPreview(ImDrawListPtr drawList)
    {
        if (!IsActive || _context == null)
            return;

        var color = IsCut ? UiColors.StatusAttention : UiColors.StatusAutomated;
        var width = 2 * T3Ui.UiScaleFactor;
        for (var i = 1; i < _trailCount; i++)
        {
            drawList.AddLine(_trail[(_trailStart + i - 1) % _trail.Length],
                             _trail[(_trailStart + i) % _trail.Length], color, width);
        }

        if (!IsCut)
        {
            if (_placementsDirty)
            {
                RerouteOperations.GetAnchorPositions(_hits, _anchorPositions, _placementBuffer);
                _placementsDirty = false;
            }

            var radius = MagGraphItem.RerouteSize.X * 0.5f * _context.View.Scale.X;
            foreach (var position in _anchorPositions)
            {
                drawList.AddCircleFilled(_context.View.TransformPosition(position), radius, color.Fade(0.7f));
            }
        }
    }

    internal bool Commit(GraphUiContext context, out string error)
    {
        error = string.Empty;
        if (!IsCurrent(context))
            return false;

        return _dragging && _hits.Count > 0 && RerouteOperations.TryApply(context, IsCut, _hits, out error);
    }

    internal void Cancel()
    {
        IsActive = false;
        _dragging = false;
        _hasSegment = false;
        _placementsDirty = false;
        _hits.Clear();
        _hitSet.Clear();
        _anchorPositions.Clear();
        _trailStart = _trailCount = 0;
        _connection = null;
        _context = null;
    }

    /*
     * Tests screen-space segments with a pixel tolerance and returns a point on the cable.
     * Endpoint distance checks also cover near misses, parallel segments, and zero-length markers.
     */
    internal static bool TryIntersectSegments(Vector2 strokeStart, Vector2 strokeEnd,
                                             Vector2 cableStart, Vector2 cableEnd,
                                             float tolerance, out Vector2 crossing)
    {
        crossing = default;
        var strokeMin = Vector2.Min(strokeStart, strokeEnd) - new Vector2(tolerance);
        var strokeMax = Vector2.Max(strokeStart, strokeEnd) + new Vector2(tolerance);
        var cableMin = Vector2.Min(cableStart, cableEnd);
        var cableMax = Vector2.Max(cableStart, cableEnd);
        if (strokeMax.X < cableMin.X || strokeMin.X > cableMax.X
            || strokeMax.Y < cableMin.Y || strokeMin.Y > cableMax.Y)
            return false;

        var stroke = strokeEnd - strokeStart;
        var cable = cableEnd - cableStart;
        var denominator = Cross(stroke, cable);
        if (MathF.Abs(denominator) > 0.00001f)
        {
            var delta = cableStart - strokeStart;
            var alongStroke = Cross(delta, cable) / denominator;
            var alongCable = Cross(delta, stroke) / denominator;
            if (alongStroke >= 0 && alongStroke <= 1 && alongCable >= 0 && alongCable <= 1)
            {
                crossing = cableStart + alongCable * cable;
                return true;
            }
        }

        var limit = tolerance * tolerance;
        crossing = ClosestPoint(cableStart, cableEnd, strokeStart);
        if (Vector2.DistanceSquared(crossing, strokeStart) <= limit)
            return true;

        crossing = ClosestPoint(cableStart, cableEnd, strokeEnd);
        if (Vector2.DistanceSquared(crossing, strokeEnd) <= limit)
            return true;

        if (Vector2.DistanceSquared(ClosestPoint(strokeStart, strokeEnd, cableStart), cableStart) <= limit)
        {
            crossing = cableStart;
            return true;
        }

        crossing = cableEnd;
        return Vector2.DistanceSquared(ClosestPoint(strokeStart, strokeEnd, cableEnd), cableEnd) <= limit;
    }

    private void TestDrawnPath(ImDrawListPtr drawList)
    {
        if (!IsActive || _context == null || _connection == null || drawList._Path.Size < 2)
            return;

        var occurrence = RerouteOperations.Capture(_connection);
        if (!_hitSet.Contains(occurrence) && _hasSegment)
        {
            var clipMin = drawList.GetClipRectMin();
            var clipMax = drawList.GetClipRectMax();
            for (var i = 1; i < drawList._Path.Size; i++)
            {
                var start = drawList._Path[i - 1];
                var end = drawList._Path[i];
                // Hidden cable portions must not receive hits through clipped canvas content.
                if (!ClipSegment(ref start, ref end, clipMin, clipMax)
                    || !TryIntersectSegments(_previous, _current, start, end, HitTolerance, out var crossing))
                    continue;

                AddHit(crossing);
                break;
            }
        }

        if (_hitSet.Contains(occurrence))
        {
            var color = IsCut ? UiColors.StatusAttention : UiColors.StatusAutomated;
            drawList.AddPolyline(ref drawList._Path[0], drawList._Path.Size, color.Fade(0.8f),
                                 ImDrawFlags.None, 6 * T3Ui.UiScaleFactor);
        }
    }

    private void AddHit(Vector2 crossing)
    {
        if (_connection == null || _context == null)
            return;

        var occurrence = RerouteOperations.Capture(_connection);
        if (!_hitSet.Add(occurrence))
            return;

        _hits.Add(new RerouteOperations.StrokeHit(occurrence, _context.View.InverseTransformPositionFloat(crossing)));
        _placementsDirty = true;
    }

    private void AddTrailPoint(Vector2 position)
    {
        if (_trailCount == _trail.Length)
        {
            _trailStart = (_trailStart + 1) % _trail.Length;
            _trailCount--;
        }

        _trail[(_trailStart + _trailCount) % _trail.Length] = position;
        _trailCount++;
    }

    private static Vector2 ClosestPoint(Vector2 start, Vector2 end, Vector2 point)
    {
        var direction = end - start;
        var lengthSquared = direction.LengthSquared();
        return lengthSquared <= 0.000001f
                   ? start
                   : start + direction * Math.Clamp(Vector2.Dot(point - start, direction) / lengthSquared, 0, 1);
    }

    private static bool ClipSegment(ref Vector2 start, ref Vector2 end, Vector2 min, Vector2 max)
    {
        var direction = end - start;
        var enter = 0f;
        var leave = 1f;
        if (!ClipAxis(start.X, direction.X, min.X, max.X, ref enter, ref leave)
            || !ClipAxis(start.Y, direction.Y, min.Y, max.Y, ref enter, ref leave))
            return false;

        end = start + direction * leave;
        start += direction * enter;
        return true;
    }

    private static bool ClipAxis(float start, float direction, float min, float max, ref float enter, ref float leave)
    {
        if (direction == 0)
            return start >= min && start <= max;

        var first = (min - start) / direction;
        var last = (max - start) / direction;
        enter = MathF.Max(enter, MathF.Min(first, last));
        leave = MathF.Min(leave, MathF.Max(first, last));
        return enter <= leave;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static float HitTolerance => 4 * T3Ui.UiScaleFactor;

    private readonly List<RerouteOperations.StrokeHit> _hits = new();
    private readonly HashSet<RerouteOperations.ConnectionOccurrence> _hitSet = new();
    private readonly List<Vector2> _anchorPositions = new();
    private readonly RerouteOperations.AnchorPlacementBuffer _placementBuffer = new();
    private readonly Vector2[] _trail = new Vector2[128];
    private GraphUiContext? _context;
    private MagGraphConnection? _connection;
    private Guid _compositionId;
    private int _version;
    private Vector2 _start;
    private Vector2 _previous;
    private Vector2 _current;
    private int _trailStart;
    private int _trailCount;
    private bool _dragging;
    private bool _hasSegment;
    private bool _placementsDirty;
}
