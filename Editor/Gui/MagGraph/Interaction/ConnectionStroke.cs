#nullable enable
using ImGuiNET;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Tracks a cut or reroute gesture against the tessellated paths drawn by the canvas.
/// Mouse segments and the bounded trail use screen coordinates; hits and anchor positions use canvas coordinates.
/// Graph identity/version invalidate stale gestures, and reusable buffers retain hits until release or cancellation.
/// </summary>
internal sealed class ConnectionStroke
{
    /// <summary>Creates the reusable path observer and reserves gesture buffers.</summary>
    internal ConnectionStroke()
    {
        ObservePath = TestDrawnPath;
    }

    /// <summary>
    /// Reused observer invoked after borrowing a connection and before PathStroke consumes the path.
    /// Hit testing and highlighting leave the pending path intact for the normal wire draw.
    /// </summary>
    internal Action<ImDrawListPtr> ObservePath { get; }
    /// <summary>Whether this collector owns an active gesture context.</summary>
    internal bool IsActive { get; private set; }
    /// <summary>Whether the gesture removes wires instead of inserting typed anchors.</summary>
    internal bool IsCut { get; private set; }

    /// <summary>Starts a gesture against the current composition and reserves buffers for its visible connections.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="cut">True to cut crossed wires; false to insert typed reroutes.</param>
    /// <param name="position">Initial mouse position in screen coordinates.</param>
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

    /// <summary>Checks that the owning view, editable composition, and graph version still match the gesture.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <returns>True while the same editable composition and version still own the active gesture.</returns>
    internal bool IsCurrent(GraphUiContext context)
    {
        return IsActive
               && ReferenceEquals(_context, context)
               && context.CompositionInstance.Symbol.Id == _compositionId
               && context.CompositionInstance.Symbol.VersionCounter == _version
               && !context.CompositionInstance.Symbol.SymbolPackage.IsReadOnly;
    }

    /// <summary>Advances the screen-space stroke segment after the drag threshold is crossed.</summary>
    /// <param name="position">Current mouse position in screen coordinates.</param>
    /// <param name="dragThreshold">Minimum screen-space distance from the press position before collecting hits.</param>
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

    /// <summary>Borrows the persistent wire being drawn; the canvas clears it when the pass ends, including on failure.</summary>
    /// <param name="connection">Wire whose path is about to be drawn, or null to release the borrowed reference.</param>
    internal void SetConnection(MagGraphConnection? connection)
    {
        _connection = connection;
    }

    /// <summary>Tests and highlights the snapped marker for the borrowed wire using the existing screen-space radii.</summary>
    /// <param name="drawList">ImGui draw list receiving the screen-space geometry.</param>
    /// <param name="center">Center of the snapped connection marker in screen coordinates.</param>
    /// <param name="canvasScale">Canvas zoom factor used to scale the graph geometry.</param>
    internal void DrawMarker(ImDrawListPtr drawList, Vector2 center, float canvasScale)
    {
        if (_connection == null)
            return;

        TestMarker(drawList, center, 7 * canvasScale);
        if (Contains(_connection))
        {
            var color = IsCut ? UiColors.StatusAttention : UiColors.StatusAutomated;
            drawList.AddCircle(center, 9 * canvasScale, color, 12, 2 * T3Ui.UiScaleFactor);
        }
    }

    /// <summary>Intersects the visible part of a snapped marker with the current stroke segment.</summary>
    /// <param name="drawList">Draw list whose clip rectangle limits the hittable marker region.</param>
    /// <param name="center">Marker center in screen coordinates.</param>
    /// <param name="radius">Marker hit radius in screen pixels.</param>
    private void TestMarker(ImDrawListPtr drawList, Vector2 center, float radius)
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

    /// <summary>Checks whether this exact ordered wire occurrence has already been hit.</summary>
    /// <param name="connection">Ordered wire occurrence to look up in the collected hit set.</param>
    /// <returns>True when an active gesture has already hit this exact occurrence.</returns>
    internal bool Contains(MagGraphConnection connection)
    {
        return IsActive && _hitSet.Contains(RerouteOperations.Capture(connection));
    }

    /// <summary>Draws the screen-space trail and cached canvas-space anchor placements.</summary>
    /// <param name="drawList">ImGui draw list receiving the screen-space geometry.</param>
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

    /// <summary>Applies collected hits only while the original editable graph is current; the caller ends the gesture.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="error">Failure explanation from applying the edit; empty if no edit was attempted or no error was reported.</param>
    /// <returns>True when a valid, nonempty gesture applies its routing or cutting edit.</returns>
    internal bool Commit(GraphUiContext context, out string error)
    {
        error = string.Empty;
        if (!IsCurrent(context))
            return false;

        return _dragging && _hits.Count > 0 && RerouteOperations.TryApply(context, IsCut, _hits, out error);
    }

    /// <summary>Releases borrowed graph references and clears collected hits while retaining reusable storage.</summary>
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

    /// <summary>
    /// Tests screen-space segments with a pixel tolerance and returns a point on the cable.
    /// Endpoint distance checks also cover near misses, parallel segments, and zero-length markers.
    /// </summary>
    /// <param name="strokeStart">Start of the mouse stroke segment in screen coordinates.</param>
    /// <param name="strokeEnd">End of the mouse stroke segment in screen coordinates.</param>
    /// <param name="cableStart">Start of the cable segment in screen coordinates.</param>
    /// <param name="cableEnd">End of the cable segment in screen coordinates.</param>
    /// <param name="tolerance">Maximum separation in screen pixels accepted as a hit.</param>
    /// <param name="crossing">Point on the cable accepted as the hit when true; default when false.</param>
    /// <returns>True when the segments intersect or come within the supplied tolerance.</returns>
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

    /// <summary>Tests the current stroke against the renderer path after clipping each segment to the visible rectangle.</summary>
    /// <param name="drawList">Draw list containing the pending tessellated connection path and its clip rectangle.</param>
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

    /// <summary>Records a wire occurrence once and invalidates its grouped preview placement.</summary>
    /// <param name="crossing">Accepted crossing point in screen coordinates, converted to canvas coordinates for anchor placement.</param>
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

    /// <summary>Appends a screen position to the bounded trail, replacing its oldest point when full.</summary>
    /// <param name="position">Screen-space mouse position appended to the bounded preview trail.</param>
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

    /// <summary>Projects a point onto a finite segment, handling a zero-length segment.</summary>
    /// <param name="start">First endpoint of the segment.</param>
    /// <param name="end">Second endpoint of the segment.</param>
    /// <param name="point">Point to project onto the segment.</param>
    /// <returns>Closest point on the segment, clamped to its endpoints.</returns>
    private static Vector2 ClosestPoint(Vector2 start, Vector2 end, Vector2 point)
    {
        var direction = end - start;
        var lengthSquared = direction.LengthSquared();
        return lengthSquared <= 0.000001f
                   ? start
                   : start + direction * Math.Clamp(Vector2.Dot(point - start, direction) / lengthSquared, 0, 1);
    }

    /// <summary>Clips a segment to the draw rectangle before hit testing.</summary>
    /// <param name="start">Segment start, replaced with the clipped start when clipping succeeds.</param>
    /// <param name="end">Segment end, replaced with the clipped end when clipping succeeds.</param>
    /// <param name="min">Minimum corner of the clipping rectangle in the same coordinates as the segment.</param>
    /// <param name="max">Maximum corner of the clipping rectangle.</param>
    /// <returns>True when a nonempty part of the segment lies inside the rectangle.</returns>
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

    /// <summary>Narrows the segment entry and exit parameters for one clipping boundary.</summary>
    /// <param name="start">Segment start coordinate on the axis being clipped.</param>
    /// <param name="direction">End coordinate minus start coordinate on this axis.</param>
    /// <param name="min">Lower clipping bound on the axis.</param>
    /// <param name="max">Upper clipping bound on the axis.</param>
    /// <param name="enter">Earliest accepted segment parameter, narrowed by this axis.</param>
    /// <param name="leave">Latest accepted segment parameter, narrowed by this axis.</param>
    /// <returns>True when this axis leaves a nonempty segment interval.</returns>
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

    /// <summary>Returns the signed two-dimensional cross product used by segment intersection.</summary>
    /// <param name="a">First vector in the two-dimensional cross product.</param>
    /// <param name="b">Second vector in the two-dimensional cross product.</param>
    /// <returns>Signed scalar cross product of the two vectors.</returns>
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    /// <summary>Screen-space hit tolerance scaled with the user interface.</summary>
    private static float HitTolerance => 4 * T3Ui.UiScaleFactor;

    /// <summary>Distinct wire occurrences and their canvas-space hit positions in encounter order.</summary>
    private readonly List<RerouteOperations.StrokeHit> _hits = new();
    /// <summary>Deduplicates occurrences without scanning the ordered hit list.</summary>
    private readonly HashSet<RerouteOperations.ConnectionOccurrence> _hitSet = new();
    /// <summary>Cached canvas-space placements shared by preview and commit.</summary>
    private readonly List<Vector2> _anchorPositions = new();
    /// <summary>Reusable source-group storage for preview placement.</summary>
    private readonly RerouteOperations.AnchorPlacementBuffer _placementBuffer = new();
    /// <summary>Bounded ring of screen-space points used to draw the gesture trail.</summary>
    private readonly Vector2[] _trail = new Vector2[128];
    /// <summary>Borrowed owning context, cleared when the gesture ends.</summary>
    private GraphUiContext? _context;
    /// <summary>Borrowed persistent wire currently being drawn; cleared at the end of the connection pass.</summary>
    private MagGraphConnection? _connection;
    /// <summary>Composition identity captured when the gesture starts.</summary>
    private Guid _compositionId;
    /// <summary>Graph structure version captured to reject stale hits.</summary>
    private int _version;
    /// <summary>Initial screen position used to enforce the drag threshold.</summary>
    private Vector2 _start;
    /// <summary>Start of the screen-space segment tested this frame.</summary>
    private Vector2 _previous;
    /// <summary>Latest screen position and end of the segment tested this frame.</summary>
    private Vector2 _current;
    /// <summary>Index of the oldest valid point in the trail ring.</summary>
    private int _trailStart;
    /// <summary>Number of valid points in the trail ring.</summary>
    private int _trailCount;
    /// <summary>Whether movement has crossed the drag threshold.</summary>
    private bool _dragging;
    /// <summary>Whether the latest update supplies a nonzero segment for hit testing.</summary>
    private bool _hasSegment;
    /// <summary>Whether newly collected hits require preview placement to be recalculated.</summary>
    private bool _placementsDirty;
}
