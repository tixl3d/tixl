#nullable enable
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The one snapping model of the setup canvases: a dragged rectangle's edges and centre catch on the
/// candidates a caller collected — the container's edges and centre, every sibling's — within a constant
/// screen distance, and a nearly-straight move locks to its axis. The same rules for surfaces on the Board,
/// regions in their parent, patches on the canvas and slices on their source, so they all feel alike.
/// Candidates live in preallocated lists: this runs inside drags.
/// </summary>
internal sealed class RectSnapping
{
    /// <summary>Which axis a move locked to; the guide is drawn along it.</summary>
    public enum AxisLocks
    {
        None,
        X, // moves along X only
        Y, // moves along Y only
    }

    public enum Axes
    {
        X,
        Y,
    }

    /// <summary>Screen distance within which an edge catches, before UI scaling.</summary>
    public const float ThresholdPixels = 7f;

    public void Clear()
    {
        _xs.Clear();
        _ys.Clear();
    }

    /// <summary>A rectangle's two edges and its centre per axis — what one container or sibling offers.</summary>
    public void AddRectEdgesAndCentre(Vector2 min, Vector2 max)
    {
        _xs.Add(min.X);
        _xs.Add((min.X + max.X) * 0.5f);
        _xs.Add(max.X);
        _ys.Add(min.Y);
        _ys.Add((min.Y + max.Y) * 0.5f);
        _ys.Add(max.Y);
    }

    /// <summary>A rectangle's edges only — cards on the Board, whose centres are not worth aligning to.</summary>
    public void AddRectEdges(Vector2 min, Vector2 max)
    {
        _xs.Add(min.X);
        _xs.Add(max.X);
        _ys.Add(min.Y);
        _ys.Add(max.Y);
    }

    public void AddY(float y)
    {
        _ys.Add(y);
    }

    /// <summary>
    /// The snap distance in the projection's own units, per axis, measured with short probes of
    /// <paramref name="probeLength"/> around <paramref name="probeAt"/>. Measured locally, not across the whole
    /// space: a rectified or keystoned view scales X and Y differently, and under perspective the scale varies
    /// across the surface, so only a local measurement feels like the same few pixels everywhere.
    /// </summary>
    public static Vector2 ThresholdFor(ICanvasProjection projection, Vector2 probeAt, float probeLength)
    {
        var origin = projection.CanvasToScreen(probeAt);
        var alongX = projection.CanvasToScreen(probeAt + new Vector2(probeLength, 0));
        var alongY = projection.CanvasToScreen(probeAt + new Vector2(0, probeLength));

        var wantedPixels = ThresholdPixels * T3Ui.UiScaleFactor;
        var pixelsX = Vector2.Distance(origin, alongX);
        var pixelsY = Vector2.Distance(origin, alongY);
        return new Vector2(pixelsX > 0.001f ? probeLength / pixelsX * wantedPixels : 0f,
                           pixelsY > 0.001f ? probeLength / pixelsY * wantedPixels : 0f);
    }

    /// <summary>
    /// Nearest candidate on <paramref name="axis"/> to any of <paramref name="anchors"/> (a rectangle offers its
    /// two edges and its centre), returning the offset that lands on it and the coordinate hit, so a guide
    /// can be drawn — or the edge can be assigned the exact float, where neighbours must share it.
    /// </summary>
    public bool TrySnap(Axes axis, ReadOnlySpan<float> anchors, float threshold, out float offset, out float target)
    {
        offset = 0;
        target = 0;
        var candidates = axis == Axes.X ? _xs : _ys;
        var bestDistance = threshold;
        var found = false;

        for (var a = 0; a < anchors.Length; a++)
        {
            var anchor = anchors[a];
            for (var c = 0; c < candidates.Count; c++)
            {
                var candidate = candidates[c];
                var delta = candidate - anchor;
                var distance = MathF.Abs(delta);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                offset = delta;
                target = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Flattens a nearly-straight move onto its axis — but only within a constant screen-space budget
    /// (1.5× the snap threshold). A plain direction cone widens with drag distance, and on a long drag it
    /// captures from 100px+ away, which reads as violent snapping.
    /// </summary>
    public static AxisLocks LockAxis(ref Vector2 delta, Vector2 thresholds)
    {
        var lockX = MathF.Abs(delta.X) > MathF.Abs(delta.Y) * 4 && MathF.Abs(delta.Y) < thresholds.Y * 1.5f;
        var lockY = MathF.Abs(delta.Y) > MathF.Abs(delta.X) * 4 && MathF.Abs(delta.X) < thresholds.X * 1.5f;
        if (lockX)
        {
            delta.Y = 0;
            return AxisLocks.X;
        }

        if (lockY)
        {
            delta.X = 0;
            return AxisLocks.Y;
        }

        return AxisLocks.None;
    }

    private readonly List<float> _xs = [];
    private readonly List<float> _ys = [];
}
