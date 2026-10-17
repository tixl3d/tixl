using System.Numerics;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Turns the manual bend points of a connection into a route that is drawn like the automatic cables:
/// horizontal and vertical segments joined by arcs instead of free diagonal lines.
/// </summary>
/// <remarks>
/// <para>
/// Each pair of consecutive points is joined by an orthogonal connector of two runs. Which axis comes
/// first alternates from one connector to the next, so every bend point is reached with one run and
/// left with the other: the point becomes a real corner instead of a spot the cable drifts past. The
/// route therefore only ever turns through right angles, which is what lets the corners be rounded
/// like the rest of the graph.
/// </para>
/// <para>
/// How round a corner ends up is decided per corner from the "Connection radius" setting and from the
/// length of the two runs that meet there, and the arc is tessellated with the same "Connection
/// segments" setting the automatic drawer uses. At a segment count of one the arc collapses to the
/// straight chord across it, which is the 45 degree look that setting is meant to produce.
/// </para>
/// </remarks>
internal static class ConnectionRouteBuilder
{
    /// <summary>Most route vertices the builder can produce, so callers can size their buffers to it.</summary>
    internal const int MaximumCorners = 128;

    /// <summary>
    /// Share of the shorter of the two runs a corner's tangent may take up. The tangent is at most the
    /// radius for the right angles a route is made of, so under half of the run always fits and still
    /// leaves a straight piece between two corners that share a run.
    /// </summary>
    private const float CornerShareOfRun = 0.45f;

    /// <summary>
    /// Smallest corner, in screen pixels. The automatic drawer keeps a minimum radius of its own, so a
    /// connection never turns on a sharp corner however the user sets the radius; a rerouted cable has
    /// to behave the same way when "Connection radius" is pulled down to zero.
    /// </summary>
    private const float MinimumCornerRadiusOnScreen = 5;

    private const float MinimumSegmentLength = 0.01f;

    /// <summary>Which axis a run of the route goes along.</summary>
    private enum Axis
    {
        None,
        Horizontal,
        Vertical,
    }

    /// <summary>A rounded turn of the route: where it turns, and how far inside the corner it starts.</summary>
    private readonly struct Corner
    {
        internal Corner(Vector2 position, float tangentLength)
        {
            Position = position;
            TangentLength = tangentLength;
        }

        internal readonly Vector2 Position;

        /// <summary>
        /// How far along each run the turn begins. For the right angles a route is made of, the arc's
        /// radius equals this - see <see cref="AppendRounded"/>.
        /// </summary>
        internal readonly float TangentLength;
    }

    /// <summary>
    /// Builds the rounded, orthogonal route from the given bend points and returns how many points were
    /// written to <paramref name="result"/>. The first and last point are kept exactly.
    /// </summary>
    /// <param name="points">Source, the bend points in canvas space, and the target.</param>
    /// <param name="cornerRadiusOnScreen">
    /// The roundest a corner may get, in screen pixels. Taken from the "Connection radius" setting so a
    /// rerouted cable curves like the cables around it.
    /// </param>
    /// <param name="canvasScale">Graph zoom: arcs are tessellated and radii mirrored through it.</param>
    /// <param name="maxSegmentCount">The "Connection segments" setting, i.e. the arc tessellation cap.</param>
    internal static int Build(IReadOnlyList<Vector2> points,
                              float cornerRadiusOnScreen,
                              float canvasScale,
                              int maxSegmentCount,
                              List<Vector2> result)
    {
        result.Clear();

        if (points.Count < 2)
            return 0;

        var scale = MathF.Max(canvasScale, 0.01f);
        var cornerRadius = MathF.Max(cornerRadiusOnScreen, MinimumCornerRadiusOnScreen) / scale;

        Span<Corner> corners = stackalloc Corner[MaximumCorners];
        var cornerCount = BuildCorners(points, cornerRadius, corners);

        if (cornerCount < 2)
            return 0;

        AppendRounded(canvasScale, maxSegmentCount, corners[..cornerCount], result);
        return result.Count;
    }

    /// <summary>
    /// Places the route's right-angle turns and works out how much of the runs each of them may spend.
    /// </summary>
    private static int BuildCorners(IReadOnlyList<Vector2> points, float cornerRadius, Span<Corner> corners)
    {
        // The turns are placed first without any radius: a corner can only be sized once the runs on
        // both of its sides are known.
        Span<Vector2> vertices = stackalloc Vector2[MaximumCorners];
        var vertexCount = 0;

        vertices[vertexCount++] = points[0];

        // The run the previous connector ended on. The next one starts on the other axis, so the two
        // turns of a connector never crowd into the same spot.
        var previousTrailingAxis = Axis.None;

        for (var index = 0; index < points.Count - 1; index++)
        {
            var from = points[index];
            var to = points[index + 1];

            // Points that fall almost on top of each other would only add cusps.
            if (Vector2.DistanceSquared(from, to) < MinimumSegmentLength * MinimumSegmentLength)
                continue;

            var delta = to - from;
            var horizontalIsLonger = MathF.Abs(delta.X) > MathF.Abs(delta.Y);

            var leadingAxis = horizontalIsLonger ? Axis.Horizontal : Axis.Vertical;

            // Alternating the axis keeps every bend point a real corner of the route. It is only a
            // preference: when the run it would ask for has no length, the longer side decides.
            if (previousTrailingAxis != Axis.None && previousTrailingAxis != leadingAxis)
            {
                var forcedLength = previousTrailingAxis == Axis.Horizontal ? delta.X : delta.Y;
                if (MathF.Abs(forcedLength) > MinimumSegmentLength)
                {
                    leadingAxis = previousTrailingAxis;
                }
            }

            // Only the first run is moved: the bend point itself closes the turn, which is what makes
            // the cable pass through the point the user dropped instead of drifting past it.
            var elbow = leadingAxis == Axis.Horizontal
                            ? new Vector2(to.X, from.Y)
                            : new Vector2(from.X, to.Y);

            if (elbow != from && elbow != to && vertexCount < MaximumCorners - 2)
            {
                vertices[vertexCount++] = elbow;
            }

            vertices[vertexCount++] = to;

            var trailingLength = leadingAxis == Axis.Horizontal ? delta.Y : delta.X;
            previousTrailingAxis = MathF.Abs(trailingLength) > MinimumSegmentLength
                                       ? leadingAxis == Axis.Horizontal ? Axis.Vertical : Axis.Horizontal
                                       : Axis.None;
        }

        if (vertexCount < 2)
            return 0;

        // The ends are not turns, so they are only carried through as limits for their neighbour.
        var cornerCount = 0;
        corners[cornerCount++] = new Corner(vertices[0], 0);

        for (var index = 1; index < vertexCount - 1; index++)
        {
            var incomingLength = Vector2.Distance(vertices[index - 1], vertices[index]);
            var outgoingLength = Vector2.Distance(vertices[index], vertices[index + 1]);

            // A 90 degree turn needs a tangent as long as the arc is wide; the rest of the angle is
            // scaled in AppendRounded. Sizing it here keeps the tangent inside both runs.
            var tangentLimit = MathF.Min(incomingLength, outgoingLength) * CornerShareOfRun;
            corners[cornerCount++] = new Corner(vertices[index], MathF.Min(cornerRadius, tangentLimit));
        }

        corners[cornerCount++] = new Corner(vertices[vertexCount - 1], 0);
        return cornerCount;
    }

    /// <summary>
    /// Writes the route: straight runs with every interior vertex replaced by an arc. A vertex that
    /// continues straight, doubles back or has no radius to spend is kept as a plain point.
    /// </summary>
    private static void AppendRounded(float canvasScale, int maxSegmentCount, ReadOnlySpan<Corner> corners, List<Vector2> result)
    {
        var last = corners.Length - 1;
        result.Add(corners[0].Position);

        for (var index = 1; index < last; index++)
        {
            var previous = corners[index - 1].Position;
            var corner = corners[index].Position;
            var next = corners[index + 1].Position;

            var incoming = corner - previous;
            var outgoing = next - corner;

            var incomingLength = incoming.Length();
            var outgoingLength = outgoing.Length();
            if (incomingLength < MinimumSegmentLength || outgoingLength < MinimumSegmentLength)
            {
                // A zero-length run would only produce a cusp, but the vertex still has to stay in the
                // list: dropping it would weld two unrelated runs into one straight line.
                result.Add(corner);
                continue;
            }

            var incomingDirection = incoming / incomingLength;
            var outgoingDirection = outgoing / outgoingLength;

            var incomingAngle = MathF.Atan2(incomingDirection.Y, incomingDirection.X);
            var outgoingAngle = MathF.Atan2(outgoingDirection.Y, outgoingDirection.X);

            // Signed turn of the route: zero for a straight continuation, which is when there is nothing
            // to round. It is taken before the arc is laid out because its sign says which way the corner
            // opens, and it is kept as it is so a turn wider than half a circle keeps its own centre.
            var sweep = outgoingAngle - incomingAngle;
            if (sweep < -MathF.PI)
                sweep += 2 * MathF.PI;
            else if (sweep > MathF.PI)
                sweep -= 2 * MathF.PI;

            if (MathF.Abs(sweep) < 0.000001f)
            {
                result.Add(corner);
                continue;
            }

            var halfTurn = MathF.Abs(sweep) * 0.5f;
            var cosHalfTurn = MathF.Cos(sweep * 0.5f);
            var sinHalfTurn = MathF.Sin(halfTurn);

            // Tangent length along each run, and the arc radius that goes with it. An arc tangent to two
            // runs meets each of them radius * tan(turn / 2) away from the corner; the routes here only
            // ever turn through right angles, so the radius ends up equal to the tangent.
            var tangentLength = corners[index].TangentLength;
            if (tangentLength < 0.01f || sinHalfTurn < 0.0001f)
            {
                result.Add(corner);
                continue;
            }

            var radius = tangentLength / MathF.Tan(halfTurn);

            var tangentIn = corner - incomingDirection * tangentLength;
            var tangentOut = corner + outgoingDirection * tangentLength;

            // The centre sits where the perpendicular from each tangent point meets, which is along the
            // bisector of the two runs at radius / sin(turn / 2) from the corner, on the inside of the
            // turn. For a right angle that is radius * sqrt(2), the corner of the square the two tangent
            // points and the centre form.
            var bisector = Vector2.Normalize(Vector2.Normalize(tangentIn - corner) + Vector2.Normalize(tangentOut - corner));
            var center = corner + bisector * (radius / sinHalfTurn) * MathF.Sign(cosHalfTurn);

            var startAngle = MathF.Atan2(tangentIn.Y - center.Y, tangentIn.X - center.X);

            // Same tessellation rule as the automatic drawer: as many segments as the arc's angle and the
            // zoom suggest, capped by the "Connection segments" setting. At a cap of one the arc collapses
            // to the straight chord across the corner.
            var segmentCount = ComputeSegmentCount(MathF.Abs(sweep), canvasScale, maxSegmentCount);

            for (var step = 0; step <= segmentCount; step++)
            {
                var angle = startAngle + sweep * step / segmentCount;
                result.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
            }
        }

        result.Add(corners[last].Position);
    }

    private static int ComputeSegmentCount(float arcLengthRadians, float canvasScale, int maxSegmentCount)
    {
        // Mirrors GraphConnectionDrawer.ComputerSegmentCount, so both kinds of cable tessellate alike.
        var circleResolution = (int)canvasScale.RemapAndClamp(0.2f, 1.5f, 6, 15);
        return Math.Clamp((int)(arcLengthRadians * circleResolution), 1, Math.Max(maxSegmentCount, 1));
    }
}
