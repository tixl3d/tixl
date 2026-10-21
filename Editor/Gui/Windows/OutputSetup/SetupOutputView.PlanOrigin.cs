#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The stage origin on a floor plan's Board card: a ring at the plan's (0,0) with an arrow along its front. Every
/// plan card shows the same origin, since all plans share the stage's frame. Dragging the ring moves it, snapping to
/// the plan's corners, edge middles and centre; its menu turns the front. See <see cref="StageOrigin"/>.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// Drawn after the plan's corners and edge dots: the first item claims an overlapping hover, so a corner lying
    /// on the origin keeps its own middle and the ring is grabbed around it.
    /// </summary>
    private void DrawPlanOrigin(Setup setup, FloorPlan plan, Vector2 origin, bool isSelected)
    {
        var scale = T3Ui.UiScaleFactor;
        var dragging = _gesture.Is(GestureKinds.PlanOrigin, plan.Id);

        var style = CanvasPointHandle.Style.Default(Color.TransparentBlack, CanvasPointHandle.Shapes.Circle, editable: !IsDrawingPlan);
        style.ActiveColor = Color.TransparentBlack; // the ring below is the glyph; a filled disc would hide a corner under it
        style.Radius = OriginRingRadius;

        var position = origin;
        ImGui.PushID(PlanOriginHandleId);
        var phase = CanvasPointHandle.Draw(ref position, _boardProjection, style);
        ImGui.PopID();

        var hovered = style.IsEditable && (ImGui.IsItemHovered() || ImGui.IsItemActive());
        if (hovered)
        {
            _hoveredPlanHandlePlanId = plan.Id;
            if (!dragging)
                CustomComponents.TooltipForLastItem("Stage origin",
                                                    "Where a scene's origin lands, on the floor; the arrow is its front. Drag to move it, right-click to turn it.");
        }

        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                BeginGesture(setup, GestureKinds.PlanOrigin, "Move origin", plan.Id);
                break;

            case CanvasPointHandle.DragPhases.Dragging when dragging:
            {
                var local = position - origin;
                if (!ImGui.GetIO().KeyShift)
                    SnapPlanOrigin(plan, ref local);

                // The move re-bases the plan onto the new origin and shifts its card by as much, so the room stays put.
                if (local.LengthSquared() > 0.00000001f)
                    StageOrigin.Move(setup, local, Vector2.UnitY);

                position = origin + local;
                CanvasPointHandle.ReportSnappedPosition(_boardProjection, position);
                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                EndGesture(setup);
                break;
        }

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(PlanOriginMenuId);

        DrawPlanOriginGlyph(position, (hovered || dragging ? UiColors.ForegroundFull : isSelected ? UiColors.Text : UiColors.TextMuted).Fade(_boardLayerFade),
                            scale);

        if (SetupPopup.Begin(PlanOriginMenuId))
        {
            CustomComponents.MenuItemsFlushLeft = true;
            var hasCentre = StageOrigin.TryGetPlanCentre(plan, out var centre);
            if (CustomComponents.DrawMenuItem(1, "Move to Plan Centre", isEnabled: hasCentre))
                SetupUndo.RunUndoable("Move origin", setup, () => StageOrigin.Move(setup, centre, Vector2.UnitY));

            CustomComponents.DrawMenuGroupLabel("Front");
            if (CustomComponents.DrawMenuItem(2, "Make Left Side the Front"))
                SetupUndo.RunUndoable("Turn origin", setup, () => StageOrigin.Move(setup, Vector2.Zero, -Vector2.UnitX));

            if (CustomComponents.DrawMenuItem(3, "Make Right Side the Front"))
                SetupUndo.RunUndoable("Turn origin", setup, () => StageOrigin.Move(setup, Vector2.Zero, Vector2.UnitX));

            if (CustomComponents.DrawMenuItem(4, "Make Back the Front"))
                SetupUndo.RunUndoable("Turn origin", setup, () => StageOrigin.Move(setup, Vector2.Zero, -Vector2.UnitY));

            CustomComponents.MenuItemsFlushLeft = false;
            SetupPopup.End();
        }
    }

    /** A ring with a dot at the origin and an arrow from it along the plan's +Y, the stage's front. */
    private void DrawPlanOriginGlyph(Vector2 position, Color color, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var screen = _boardProjection.CanvasToScreen(position);
        var front = _boardProjection.CanvasToScreen(position + Vector2.UnitY) - screen;
        front /= MathF.Max(front.Length(), 0.001f);
        var side = new Vector2(-front.Y, front.X);

        var ringRadius = OriginRingRadius * scale;
        var thickness = MathF.Max(1, MathF.Round(1.5f * scale));
        dl.AddCircle(screen, ringRadius, color, 0, thickness);
        dl.AddCircleFilled(screen, 1.5f * scale, color);

        var arrowStart = screen + front * ringRadius;
        var arrowTip = screen + front * (ringRadius + OriginArrowLength * scale);
        var headSize = 5 * scale;
        dl.AddLine(arrowStart, arrowTip - front * headSize, color, thickness);
        dl.AddTriangleFilled(arrowTip, arrowTip - front * headSize * 1.6f + side * headSize, arrowTip - front * headSize * 1.6f - side * headSize, color);
    }

    /** Pulls a dragged origin onto the plan's corners, edge middles or centre, whichever is nearest within the snap threshold. */
    private void SnapPlanOrigin(FloorPlan plan, ref Vector2 local)
    {
        var bestDistance = BoardSnapThreshold();
        var snapped = local;
        for (var vertexIndex = 0; vertexIndex < plan.Vertices.Count; vertexIndex++)
            ConsiderOriginSnap(plan.Vertices[vertexIndex], local, ref bestDistance, ref snapped);

        for (var segment = 0; segment < plan.SegmentCount; segment++)
        {
            plan.GetSegment(segment, out var start, out var end);
            ConsiderOriginSnap((start + end) * 0.5f, local, ref bestDistance, ref snapped);
        }

        if (StageOrigin.TryGetPlanCentre(plan, out var centre))
            ConsiderOriginSnap(centre, local, ref bestDistance, ref snapped);

        local = snapped;
    }

    private static void ConsiderOriginSnap(Vector2 candidate, Vector2 local, ref float bestDistance, ref Vector2 snapped)
    {
        var distance = (candidate - local).Length();
        if (distance >= bestDistance)
            return;

        bestDistance = distance;
        snapped = candidate;
    }

    /** Unscaled screen pixels; larger than a corner handle, so the ring stays grabbable around a corner on the origin. */
    private const float OriginRingRadius = 9;

    /** Unscaled screen pixels from the ring to the arrow's tip. */
    private const float OriginArrowLength = 22;

    private const int PlanOriginHandleId = 2000;
    private const string PlanOriginMenuId = "##planOriginMenu";
}
