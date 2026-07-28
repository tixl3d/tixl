#nullable enable
using System.Collections.Generic;
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.ProjectHandling;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Measure-and-straighten for <see cref="SetupOutputView"/>: annotation lines traced across features that are
/// straight in reality, the corner-pin refine that turns those into a rectified surface, and "apply lengths"
/// that re-meters it from measured distances. All of it rides the corner pin (endpoints stored in surface
/// meters), so the metric changes without anything moving on the wall.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// Measuring lines on the straightened surface. Draw one across a feature whose real length you measured
    /// on the wall, type that length, and "apply lengths" re-meters the surface from it. Endpoints are stored
    /// in surface meters, so they ride the corner pin like everything else — which is what lets the metric
    /// change without anything moving on the wall.
    /// </summary>
    private void DrawAnnotations(ImDrawListPtr dl, Surface carrier, Surface.OutputMapping carrierMapping,
                                 Homography rToView, Homography rToOutput, Vector2 viewMin, bool editable, float fade)
    {
        if (fade < 0.01f
            || !SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, out var surfaceToOutput)
            || !SurfaceGeometry.TryGetOutputToSurface(carrier, carrierMapping, out var outputToSurface))
        {
            return;
        }

        // Surface meters → the framed view the canvas draws in, and back. Local functions called directly,
        // so they cost nothing per frame.
        Vector2 ToView(Vector2 inSurface) => rToView.TransformPoint(surfaceToOutput.TransformPoint(inSurface)) - viewMin;
        Vector2 ToSurface(Vector2 inView) => outputToSurface.TransformPoint(rToOutput.TransformPoint(inView + viewMin));

        var canEdit = editable && _viewMorph < 1.5f;
        var annotations = carrier.Annotations;

        // Arming the tool turns the next drag on empty canvas into a new line, then disarms — "create then
        // edit", so you don't have to remember to leave a mode.
        if (canEdit && _measureArmed && _measureDraftIndex < 0
            && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered())
        {
            var start = ToSurface(_projection.ScreenToCanvas(ImGui.GetMousePos()));

            // Projected so the start point can be found against a real feature before committing to it. The
            // composite for this frame is already rendered, so it lands one frame later — imperceptible for a
            // cursor, and the same lag the hover cross-highlight accepts.
            OutputManager.SetAimPoint(carrier.Id, start);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                annotations.Add(new LineAnnotation { P1 = start, P2 = start });
                _measureDraftIndex = annotations.Count - 1;
            }
        }

        if (_measureDraftIndex >= 0)
        {
            if (_measureDraftIndex >= annotations.Count)
            {
                _measureDraftIndex = -1;
            }
            else if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                annotations[_measureDraftIndex].P2 = ToSurface(_projection.ScreenToCanvas(ImGui.GetMousePos()));
            }
            else
            {
                // A click without a drag leaves a zero-length line, which is just litter.
                var draft = annotations[_measureDraftIndex];
                if ((draft.P2 - draft.P1).Length() < 0.001f)
                {
                    annotations.RemoveAt(_measureDraftIndex);
                }
                else
                {
                    OutputSetupHandling.SaveActive();
                }

                _measureDraftIndex = -1;
                _measureArmed = false;
            }
        }

        var scale = T3Ui.UiScaleFactor;

        // The lines sit over a projected grid on a real wall, so they're genuinely hard to see. A white blink
        // draws the eye to the active handle/line without hiding the alignment colour the rest of the time.
        var blink = MathF.Sin((float)ImGui.GetTime() * BlinkRate) * 0.5f + 0.5f;
        var white = UiColors.ForegroundFull.Fade(fade);
        var nextDragIndex = -1;

        for (var i = 0; i < annotations.Count; i++)
        {
            var annotation = annotations[i];
            var measured = (annotation.P2 - annotation.P1).Length();
            var isMeasurement = annotation.LengthInMeters > 0;

            // The line's own colour reports how close it is to the axis it claims — this is the readout that
            // tells you whether straighten has converged, so it belongs on the line rather than in a panel.
            IsHorizontal(annotation, out var deviation);
            var color = OutputManager.AlignmentColor(deviation).Fade(fade);

            ImGui.PushID(i);

            var p1 = ToView(annotation.P1);
            var p2 = ToView(annotation.P2);

            // While dragging (this frame's draft, or an endpoint grabbed last frame) the line pulses white ↔ its
            // alignment colour and thickens, so it's unmistakable under the cursor — in the editor overlay and,
            // via the projected composite, on the wall itself.
            var isDragging = i == _measureDraftIndex || i == _measureDragIndex;
            if (isDragging)
                OutputManager.EmphasizeAnnotation(carrier.Id, i);

            var lineColor = isDragging ? Color.Mix(color, white, blink) : color;
            var lineWidth = (isDragging ? 6f : 2f) * scale;
            dl.AddLine(_projection.CanvasToScreen(p1), _projection.CanvasToScreen(p2), lineColor, lineWidth);

            if (canEdit && _measureDraftIndex < 0)
            {
                var style = CanvasPointHandle.Style.Default(color);
                // Idle endpoints blink white and sit larger, so they read against the grid before you grab one.
                style.Color = Color.Mix(color, white, blink);
                style.Radius = 7;

                ImGui.PushID("p1");
                var phase1 = CanvasPointHandle.Draw(ref p1, _projection, style);
                ImGui.PopID();
                ImGui.PushID("p2");
                var phase2 = CanvasPointHandle.Draw(ref p2, _projection, style);
                ImGui.PopID();

                // Pre-drag snapshot before this frame's apply, so the whole drag undoes as one step.
                if (phase1 == CanvasPointHandle.DragPhase.Started || phase2 == CanvasPointHandle.DragPhase.Started)
                    _annotationDragStart = (i, annotation.P1, annotation.P2);

                if (phase1 != CanvasPointHandle.DragPhase.None)
                    annotation.P1 = ToSurface(p1);

                if (phase2 != CanvasPointHandle.DragPhase.None)
                    annotation.P2 = ToSurface(p2);

                if (phase1 is CanvasPointHandle.DragPhase.Started or CanvasPointHandle.DragPhase.Dragging
                    || phase2 is CanvasPointHandle.DragPhase.Started or CanvasPointHandle.DragPhase.Dragging)
                    nextDragIndex = i;

                if (phase1 == CanvasPointHandle.DragPhase.Completed || phase2 == CanvasPointHandle.DragPhase.Completed)
                {
                    // Value already applied live during the drag.
                    if (_annotationDragStart is { } dragStart && dragStart.Index == i)
                        UndoRedoStack.Add(new ChangeAnnotationCommand(carrier.Id, i, dragStart.P1, dragStart.P2,
                                                                      annotation.P1, annotation.P2));

                    _annotationDragStart = null;
                    OutputSetupHandling.SaveActive();
                }
            }

            // Measuring is a separate act from aligning: a line is a reference line until double-clicking it
            // gives it a real length, and clearing that length makes it one again.
            var requestLength = canEdit && _measureDraftIndex < 0
                                && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered()
                                && IsMouseNearSegment(_projection.CanvasToScreen(p1), _projection.CanvasToScreen(p2));

            DrawAnnotationLabel(dl, annotation, i, measured, isMeasurement, _projection.CanvasToScreen((p1 + p2) * 0.5f),
                                color, canEdit, requestLength);
            ImGui.PopID();
        }

        // A handle's drag phase is only known after it's drawn (below its line), so the line reads it next frame.
        _measureDragIndex = nextDragIndex;

        if (_annotationToDelete >= 0)
        {
            if (_annotationToDelete < annotations.Count)
            {
                annotations.RemoveAt(_annotationToDelete);
                OutputSetupHandling.SaveActive();
            }

            _annotationToDelete = -1;
        }
    }

    /// <summary>
    /// The length chip at the line's middle: the typed real length once it has one, the drawn length until
    /// then. Clicking it is how a line becomes a measurement.
    /// </summary>
    private static void DrawAnnotationLabel(ImDrawListPtr dl, LineAnnotation annotation, int index, float measured, bool isMeasurement,
                                            Vector2 screen, T3.Core.DataTypes.Vector.Color color, bool canEdit, bool requestLength)
    {
        var scale = T3Ui.UiScaleFactor;

        // Only a measured line carries a chip. Labelling every line with the length it happens to span would
        // read as a measurement that was never taken.
        if (isMeasurement)
        {
            var text = $"{annotation.LengthInMeters:0.###} m";
            ImGui.PushFont(Fonts.FontSmall);
            var textSize = ImGui.CalcTextSize(text);
            ImGui.PopFont();

            var padding = new Vector2(4, 2) * scale;
            var min = screen - textSize * 0.5f - padding;
            var max = screen + textSize * 0.5f + padding;
            dl.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.7f), 3 * scale);
            ImGui.SetCursorScreenPos(min + padding);
            CustomComponents.StylizedText(text, Fonts.FontSmall, color);
        }

        if (!canEdit)
            return;

        if (requestLength)
        {
            // Seeded with the drawn length so typing a correction is a nudge, not an entry from nothing.
            _lengthEdit = annotation.LengthInMeters > 0 ? annotation.LengthInMeters : measured;
            ImGui.OpenPopup("##setLength");
        }

        if (!ImGui.BeginPopup("##setLength"))
            return;

        CustomComponents.StylizedText("Real length in meters", Fonts.FontSmall, UiColors.TextMuted);
        ImGui.SetNextItemWidth(100 * scale);
        ImGui.InputFloat("##len", ref _lengthEdit);
        if (ImGui.Button("Set") || ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            // Zero or less is how a measurement becomes an ordinary reference line again.
            annotation.LengthInMeters = _lengthEdit > 0 ? _lengthEdit : 0;
            OutputSetupHandling.SaveActive();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete"))
        {
            // Recorded, not removed: the list is being iterated, and the popup owning this button lives in it.
            _annotationToDelete = index;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>Which axis the line claims and by how much it misses it — inferred, never stored.</summary>
    private static bool IsHorizontal(LineAnnotation annotation, out float deviationInDegrees)
    {
        return LineRectifier.IsHorizontal(annotation.P1, annotation.P2, out deviationInDegrees);
    }

    /// <summary>Whether the cursor is close enough to a screen-space segment to count as being on the line.</summary>
    private static bool IsMouseNearSegment(Vector2 a, Vector2 b)
    {
        var delta = b - a;
        var lengthSquared = delta.LengthSquared();
        var mouse = ImGui.GetMousePos();
        var t = lengthSquared < 0.0001f ? 0 : Math.Clamp(Vector2.Dot(mouse - a, delta) / lengthSquared, 0f, 1f);
        return (mouse - (a + delta * t)).Length() < 6 * T3Ui.UiScaleFactor;
    }

    /// <summary>
    /// Refines the corner pin until the traced features come out level and plumb. Unlike a closed-form solve
    /// this needs no quorum per axis — the regularization toward the current quad covers what the lines leave
    /// under-determined — so three verticals and one horizontal is a usable set. The lines move with the space
    /// (they mark physical features, and those didn't move); child regions do not (their meters are a design
    /// placement, and it is exactly those meters that just became truthful).
    /// </summary>
    private static bool TryStraightenFromLines(Surface surface, Guid outputId)
    {
        var mapping = surface.OutputMappings.Find(m => m.OutputId == outputId);
        if (mapping == null || mapping.Quad.Length < 4 || surface.Annotations.Count < MinLinesToStraighten
            || !SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, out var surfaceToOutput))
        {
            return false;
        }

        // The lines in output pixels: what was actually aimed at physical features, and the one thing that
        // must not move when the corners do.
        _refineLines.Clear();
        foreach (var annotation in surface.Annotations)
        {
            var a = surfaceToOutput.TransformPoint(annotation.P1);
            var b = surfaceToOutput.TransformPoint(annotation.P2);
            _refineLines.Add(new Vector4(a.X, a.Y, b.X, b.Y));
        }

        Span<Vector2> refined = stackalloc Vector2[4];
        if (!LineRectifier.TryRefineQuad(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_refineLines),
                                         surface.SizeInMeters, mapping.Quad, refined))
        {
            return false;
        }

        for (var i = 0; i < 4; i++)
            mapping.Quad[i] = refined[i];

        if (!SurfaceGeometry.TryGetOutputToSurface(surface, mapping, out var outputToSurface))
            return false;

        // How the surface's own space just moved. Every other projector on this surface has to follow it, or
        // they would stop agreeing with the one that was just refined.
        var rectify = Homography.Multiply(outputToSurface, surfaceToOutput);
        if (rectify.TryInvert(out var inverse))
        {
            var rect = SurfaceGeometry.RectForSize(surface.SizeInMeters);
            foreach (var other in surface.OutputMappings)
            {
                if (ReferenceEquals(other, mapping) || !SurfaceGeometry.TryGetSurfaceToOutput(surface, other, out var otherToOutput))
                    continue;

                for (var i = 0; i < 4; i++)
                    other.Quad[i] = otherToOutput.TransformPoint(inverse.TransformPoint(rect[i]));
            }
        }

        // Re-expressed from their unchanged output positions: the lines mark physical features, and those
        // didn't move — only the space they are named in did.
        for (var i = 0; i < surface.Annotations.Count; i++)
        {
            var line = _refineLines[i];
            surface.Annotations[i].P1 = outputToSurface.TransformPoint(new Vector2(line.X, line.Y));
            surface.Annotations[i].P2 = outputToSurface.TransformPoint(new Vector2(line.Z, line.W));
        }

        OutputSetupHandling.SaveActive();
        return true;
    }

    /// <summary>Two lines is the least that says anything about the pin; one only pins a single direction.</summary>
    private const int MinLinesToStraighten = 2;

    /// <summary>
    /// Rescales the surface so its measured lines read their real lengths. Everything stored in this surface's
    /// meters scales with it — child regions and the lines themselves — so the projection is untouched and only
    /// the numbers become true. Each line constrains the axis it lies nearest; measuring only one axis scales
    /// both, since nothing has said the other differs.
    /// </summary>
    private static bool TryApplyLengths(Setup setup, Surface surface)
    {
        float sumX = 0, sumY = 0;
        int countX = 0, countY = 0;

        foreach (var annotation in surface.Annotations)
        {
            if (annotation.LengthInMeters <= 0)
                continue;

            // The whole segment, not its axis component: on a straightened surface those agree, and where
            // they don't the line isn't level enough to be trusted anyway.
            var length = (annotation.P2 - annotation.P1).Length();
            if (length < 0.0001f)
                continue;

            if (IsHorizontal(annotation, out _))
            {
                sumX += annotation.LengthInMeters / length;
                countX++;
            }
            else
            {
                sumY += annotation.LengthInMeters / length;
                countY++;
            }
        }

        if (countX == 0 && countY == 0)
            return false;

        // Averaged, so a second measurement of the same wall improves the answer instead of replacing it.
        // With only one axis measured there is nothing to say the other differs, so it scales along.
        var scaleX = countX > 0 ? sumX / countX : sumY / countY;
        var scaleY = countY > 0 ? sumY / countY : sumX / countX;

        ScaleSurfaceMetric(setup, surface, new Vector2(scaleX, scaleY));
        OutputSetupHandling.SaveActive();
        return true;
    }

    private static void ScaleSurfaceMetric(Setup setup, Surface surface, Vector2 scale)
    {
        surface.SizeInMeters *= scale;
        foreach (var annotation in surface.Annotations)
        {
            annotation.P1 *= scale;
            annotation.P2 *= scale;
        }

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var child = setup.Surfaces[i];
            if (child.ParentId != surface.Id)
                continue;

            child.LocalPosition *= scale;
            ScaleSurfaceMetric(setup, child, scale);
        }
    }

    // Measure/straighten state.
    private bool _measureArmed;
    private int _measureDraftIndex = -1;
    private int _measureDragIndex = -1; // endpoint grabbed last frame, so its line can emphasize this frame
    private (int Index, Vector2 P1, Vector2 P2)? _annotationDragStart; // pre-drag endpoints for the undo step

    private const float BlinkRate = 8f;
    private static float _lengthEdit;
    private static int _annotationToDelete = -1;
    private static readonly List<Vector4> _refineLines = [];
}
