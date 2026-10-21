#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// A floor plan's floor rectangle on its Board card: the outline of what the floor surface and its patch stand for,
/// and, while the floor surface is selected, a handle in the middle of each side that drags that side, snapping to
/// the plan's corners. A concave room's niche can then reach past the floor instead of stretching it.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// Drawn before the plan's corners and edge dots: with the floor selected its sides are what is being edited,
    /// so they take the hover where a side's middle lies on a wall's dot.
    /// </summary>
    private void DrawPlanFloorRect(Setup setup, SetupEntitySelection? selection, FloorPlan plan, Vector2 origin)
    {
        var floorId = plan.RaisedFloorId;
        if (!plan.IsClosed || floorId == Guid.Empty || setup.FindSurface(floorId) == null || !plan.TryGetFloorRect(out var rect))
            return;

        var floorSelected = selection?.IsSelected(SetupEntityKinds.Surface, floorId) ?? false;
        var dragging = _gesture.Is(GestureKinds.PlanFloorRect, plan.Id);
        if (plan.FloorRect == null && !floorSelected && !dragging)
            return;

        var scale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();
        var hue = SetupColors.ForKind(SetupEntityKinds.Surface);
        var color = (floorSelected ? hue : hue.Fade(0.6f)).Fade(_boardLayerFade);
        for (var corner = 0; corner < 4; corner++)
            _floorRectScreen[corner] = _boardProjection.CanvasToScreen(origin + rect.FromFractions(_floorRectCornerFractions[corner]));

        dl.AddPolyline(ref _floorRectScreen[0], 4, color, ImDrawFlags.Closed, MathF.Max(1, MathF.Round((floorSelected ? 2 : 1) * scale)));

        if (!floorSelected && !dragging)
            return;

        var style = CanvasPointHandle.Style.Default(hue, CanvasPointHandle.Shapes.Circle, editable: !IsDrawingPlan);
        for (var side = 0; side < 4; side++)
        {
            var normal = SideNormal(rect, side);
            var mid = rect.Centre + normal * (SideExtent(rect.Size, side) * 0.5f);
            var position = origin + mid;

            ImGui.PushID(FloorRectHandleId + side);
            var phase = CanvasPointHandle.Draw(ref position, _boardProjection, style);
            ImGui.PopID();

            if (style.IsEditable && (ImGui.IsItemHovered() || ImGui.IsItemActive()))
            {
                _hoveredPlanHandlePlanId = plan.Id;
                CustomComponents.TooltipForLastItem("Floor side", "Drag to set what the floor surface and its patch cover; snaps to the plan's corners, Shift moves freely.");
            }

            switch (phase)
            {
                case CanvasPointHandle.DragPhases.Started:
                    BeginGesture(setup, GestureKinds.PlanFloorRect, "Resize floor", plan.Id);
                    _floorRectSide = side;
                    _floorRectStart = rect;
                    break;

                case CanvasPointHandle.DragPhases.Dragging when dragging && _floorRectSide == side:
                {
                    var start = _floorRectStart;
                    var startExtent = SideExtent(start.Size, side);
                    var startMid = start.Centre + normal * (startExtent * 0.5f);
                    var offset = Vector2.Dot(position - origin - startMid, normal);
                    if (!ImGui.GetIO().KeyShift)
                        offset = SnapFloorSide(plan, startMid, normal, offset);

                    offset = MathF.Max(offset, SurfaceGeometry.MinSize - startExtent);
                    var resized = start;
                    resized.Centre = start.Centre + normal * (offset * 0.5f);
                    resized.Size = side % 2 == 0
                                       ? new Vector2(start.Size.X, startExtent + offset)
                                       : new Vector2(startExtent + offset, start.Size.Y);
                    plan.FloorRect = resized;
                    FloorPlanSync.Apply(setup, plan);
                    CanvasPointHandle.ReportSnappedPosition(_boardProjection, origin + startMid + normal * offset);
                    break;
                }

                case CanvasPointHandle.DragPhases.Completed:
                    EndGesture(setup);
                    break;
            }
        }
    }

    /** Sides in order bottom, right, top, left: the direction each faces, out of the rectangle. */
    private static Vector2 SideNormal(in FloorPlan.FloorRectangle rect, int side)
    {
        return side switch
                   {
                       0 => -rect.AxisY,
                       1 => rect.AxisX,
                       2 => rect.AxisY,
                       _ => -rect.AxisX,
                   };
    }

    /** How far the rectangle reaches across a side: its depth for the bottom and top, its width for the others. */
    private static float SideExtent(Vector2 size, int side) => side % 2 == 0 ? size.Y : size.X;

    /** Pulls a dragged side onto the plan corner nearest to its line, within the snap threshold. */
    private float SnapFloorSide(FloorPlan plan, Vector2 startMid, Vector2 normal, float offset)
    {
        var bestDistance = BoardSnapThreshold();
        var snapped = offset;
        for (var vertexIndex = 0; vertexIndex < plan.Vertices.Count; vertexIndex++)
        {
            var candidate = Vector2.Dot(plan.Vertices[vertexIndex] - startMid, normal);
            var distance = MathF.Abs(candidate - offset);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            snapped = candidate;
        }

        return snapped;
    }

    private static readonly Vector2[] _floorRectCornerFractions = [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY];
    private readonly Vector2[] _floorRectScreen = new Vector2[4];
    private int _floorRectSide;
    private FloorPlan.FloorRectangle _floorRectStart;
    private const int FloorRectHandleId = 3000;
}
