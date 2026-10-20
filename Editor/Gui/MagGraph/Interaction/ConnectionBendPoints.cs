using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Draws and hit-tests the draggable control points that bend a connection around the nodes it crosses.
/// </summary>
/// <remarks>
/// A bend point has to be big enough to aim at, which is why the grab radius is generous and never
/// shrinks below a screen-space minimum: on a zoomed-out graph a canvas-scaled handle would otherwise
/// be a couple of pixels across and effectively unclickable.
/// </remarks>
internal static class ConnectionBendPoints
{
    /// <summary>Radius within which a click counts as grabbing the bend point, in screen pixels.</summary>
    private const float GrabRadiusOnScreen = 11;

    /// <summary>Radius of the drawn handle, in screen pixels, before the UI scale factor.</summary>
    private const float HandleRadiusOnScreen = 4.5f;

    internal static float GrabRadius => GrabRadiusOnScreen * T3Ui.UiScaleFactor;

    /// <summary>
    /// Returns the index of the bend point under <paramref name="screenPosition"/>, or -1.
    /// </summary>
    internal static int FindBendPointAt(MagGraphView view, MagGraphConnection connection, Vector2 screenPosition)
    {
        var waypoints = connection.GetWaypoints();
        if (waypoints == null)
            return -1;

        var grabRadiusSquared = GrabRadius * GrabRadius;
        for (var index = 0; index < waypoints.Count; index++)
        {
            if (Vector2.DistanceSquared(view.TransformPosition(waypoints[index]), screenPosition) < grabRadiusSquared)
                return index;
        }

        return -1;
    }

    /// <summary>
    /// Draws the handles of a rerouted connection. The hovered or dragged handle is filled in so it
    /// reads as grabbed; the rest stay hollow rings that mark where the cable can be moved.
    /// </summary>
    internal static void DrawHandles(MagGraphView view,
                                     ImDrawListPtr drawList,
                                     MagGraphConnection connection,
                                     Color color,
                                     int hoveredIndex,
                                     int draggedIndex)
    {
        var waypoints = connection.GetWaypoints();
        if (waypoints == null)
            return;

        var radius = MathF.Max(HandleRadiusOnScreen * T3Ui.UiScaleFactor, 2);
        var outlineColor = UiColors.WindowBackground.Fade(0.8f);

        for (var index = 0; index < waypoints.Count; index++)
        {
            var positionOnScreen = view.TransformPosition(waypoints[index]);
            var isGrabbed = index == draggedIndex || index == hoveredIndex;

            if (isGrabbed)
            {
                drawList.AddCircleFilled(positionOnScreen, radius, color, 16);
                drawList.AddCircle(positionOnScreen, radius, outlineColor, 16, MathF.Max(1, T3Ui.UiScaleFactor));
            }
            else
            {
                drawList.AddCircle(positionOnScreen, radius, color, 16, MathF.Max(1, T3Ui.UiScaleFactor));
            }
        }
    }
}
