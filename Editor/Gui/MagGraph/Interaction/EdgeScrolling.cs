#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Scrolls the graph while a drag holds the pointer near or past the edge of the view, so dragged operators,
/// sections or the dragged end of a connection stay in sight. The deeper the pointer is in the edge zone, the faster it scrolls.
/// </summary>
internal static class EdgeScrolling
{
    /** Call when a drag starts. */
    internal static void Reset()
    {
        _hasBeenInside = false;
    }

    internal static void Update(ScalableCanvas canvas)
    {
        var margin = EdgeMargin * T3Ui.UiScaleFactor;
        if (canvas.WindowSize.X < margin * 4 || canvas.WindowSize.Y < margin * 4 || !ImGui.IsMousePosValid())
            return;

        var innerMin = canvas.WindowPos + new Vector2(margin);
        var innerMax = canvas.WindowPos + canvas.WindowSize - new Vector2(margin);
        var mousePos = ImGui.GetMousePos();
        var depthInEdge = new Vector2(GetDepthInEdge(mousePos.X, innerMin.X, innerMax.X),
                                      GetDepthInEdge(mousePos.Y, innerMin.Y, innerMax.Y));

        if (depthInEdge == Vector2.Zero)
        {
            _hasBeenInside = true;
            return;
        }

        // A drag that starts at the edge, like one from an op at the view's border, must not scroll
        // before the pointer has moved towards that edge.
        if (!_hasBeenInside)
            return;

        var speedFactor = Vector2.Clamp(depthInEdge / margin, new Vector2(-MaxSpeedFactor), new Vector2(MaxSpeedFactor));
        var deltaTime = Math.Clamp(ImGui.GetIO().DeltaTime, 0, 0.1f);
        canvas.ScrollByScreenPixels(speedFactor * SpeedInPixelsPerSec * T3Ui.UiScaleFactor * deltaTime);
    }

    /** Negative before <paramref name="min"/>, positive after <paramref name="max"/>, zero between. */
    private static float GetDepthInEdge(float value, float min, float max)
    {
        if (value < min)
            return value - min;

        return value > max ? value - max : 0;
    }

    /** Width of the zone along each edge in which scrolling starts. */
    private const float EdgeMargin = 30;

    /** Scroll speed with the pointer right at the window edge. */
    private const float SpeedInPixelsPerSec = 600;

    /** Further outside the window the speed keeps growing, up to this multiple. */
    private const float MaxSpeedFactor = 3;

    private static bool _hasBeenInside;
}
