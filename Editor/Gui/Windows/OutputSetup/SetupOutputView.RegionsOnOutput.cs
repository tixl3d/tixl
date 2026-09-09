#nullable enable
using ImGuiNET;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Regions as seen on the projector canvas: drawn through the parent's corner pin, edited in the parent's space.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// Outlines a Layout child using the quad already derived into <see cref="_childQuadBuffer"/>. Dimmer and
    /// thinner than a mapped surface, and without handles, because it isn't independently editable — its shape
    /// comes from the parent's corner pin plus its own rectangle in the parent's space.
    /// </summary>
    private void DrawChildRegion(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, Homography rToView,
                                 Homography rToOutput, Vector2 viewMin,
                                 Surface carrier, Surface.OutputMapping carrierMapping, Surface parent, Surface child,
                                 bool editable, float fade)
    {
        if (fade <= 0.01f)
            return;

        var isFocused = child.Id == _shownSurfaceId;

        // Multi-selection styling only — editing and the anchor stay with the focused (primary) region.
        var isSelected = isFocused
                         || (selection?.IsSelected(SetupEntitySelection.EntityKind.Surface, child.Id) ?? false);

        // The child's quad (already derived into the buffer) carried into the framed canvas. Shares the
        // surface loop's view buffer: a child is drawn and done before its parent's iteration fills it.
        var viewQuad = _viewQuad;
        for (var c = 0; c < 4; c++)
            viewQuad[c] = rToView.TransformPoint(_childQuadBuffer[c]) - viewMin;

        Span<Vector2> screen = stackalloc Vector2[4];
        for (var i = 0; i < 4; i++)
            screen[i] = _projection.CanvasToScreen(viewQuad[i]);

        var style = CornerPinHandles.Style.ForSurface(child.Name, editable && isFocused, isSelected, fade, hue: SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface));
        var childPulse = isSelected ? 0f : FrameStats.GetPulse(child.Id);
        if (childPulse > 0.001f)
            dl.AddQuadFilled(screen[0], screen[1], screen[2], screen[3],
                             SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface).Fade(childPulse * 0.15f * fade));

        // The outline carries the hover highlight, same as a top-level surface.
        CanvasDraw.QuadOutline(dl, screen, PulseColor(style.EdgeColor, childPulse), isSelected ? 2f : 1f);

        // A region has its own anchor, in its own space — mapped out through the parent's rectangle and pin.
        if (isFocused
            && SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, SurfaceGeometry.CanvasSizeOf(setup, carrierMapping.OutputId), out var carrierToOutput)
            && SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out var rectMin, out _, out _))
        {
            var anchorInCarrier = rectMin + child.AnchorInMeters;
            DrawAnchorGlyph(dl, _projection.CanvasToScreen(rToView.TransformPoint(carrierToOutput.TransformPoint(anchorInCarrier)) - viewMin), fade);
        }

        // Edited in the parent's space: the child has no projection of its own, so the parent's inverse maps
        // handles back into plain rectangle edits. Nothing here changes the parent, so the transform driving
        // this view stays put and the drag can't feed back on itself.
        var hasInverse = SurfaceGeometry.TryGetOutputToSurface(carrier, carrierMapping, SurfaceGeometry.CanvasSizeOf(setup, carrierMapping.OutputId), out var outputToSurface);
        if (!isFocused || !editable || !hasInverse)
        {
            // Still registered as a pick target — an unselected region has to stay clickable, which is the
            // only way to reach it while its parent is selected.
            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screen, child.Id, child.Name, isSelected, fade, childPulse);
            return;
        }

        ImGui.PushID(child.Id.GetHashCode());

        // The label doubles as this region's move handle, so it wins over the edge handles beneath it — unless
        // an edge crop is already live, which the cursor passing over the label mustn't drop.
        var edgeActive = _gesture.Is(GestureKinds.SurfaceResize, child.Id);
        if (!edgeActive && !string.IsNullOrEmpty(child.Name) && IsMouseOverLabel(screen, child.Name))
            style.Editable = false;

        var edgePhase = CornerPinHandles.DrawEdgeHandles(viewQuad, _projection, style, out var edge, out var edgePos);
        if (edge >= 0)
        {
            var hasProjection = SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, SurfaceGeometry.CanvasSizeOf(setup, carrierMapping.OutputId), out var parentProjection);
            SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out _, out _, out var edgeParentOrigin);
            RunGesture(edgePhase, setup, GestureKinds.SurfaceResize, "Edit region", child,
                            onStarted: () =>
                                       {
                                           _edgeStretch = ImGui.GetIO().KeyCtrl;
                                           if (!_edgeStretch)
                                               BeginContentEdit(setup, child);
                                       },
                            onDragging: () =>
                            {
                                var pos = ToParentSpace(setup, carrier, child, outputToSurface, rToOutput, viewMin, edgePos);
                                var horizontal = edge is 1 or 3;
                                float? guide = null;

                                // The dragged edge snaps to the parent's and the siblings' edges and centres.
                                if (!ImGui.GetIO().KeyShift && hasProjection)
                                {
                                    SurfaceGeometry.CollectSnapCandidates(setup, parent, child.Id, _snapXs, _snapYs);
                                    Span<float> anchor = [horizontal ? pos.X : pos.Y];
                                    var thresholds = SnapThresholds(parentProjection, rToView, viewMin, parent, edgeParentOrigin, pos);
                                    if (SurfaceGeometry.TrySnapOffset(horizontal ? _snapXs : _snapYs, anchor,
                                                                      horizontal ? thresholds.X : thresholds.Y,
                                                                      out var offset, out var target))
                                    {
                                        if (horizontal)
                                            pos.X += offset;
                                        else
                                            pos.Y += offset;

                                        guide = target;
                                    }
                                }

                                // Re-based on the pre-drag rectangle, so the crop's UV derivation never compounds.
                                _gesture.Snapshot!.Value.Restore(child);
                                SurfaceGeometry.ChildBounds(child, out var min, out var max);
                                var oldMin = min;
                                var oldMax = max;
                                switch (edge) // 0 = top … 3 = left in screen winding; parent space is Y-up
                                {
                                    case 0: max.Y = MathF.Max(pos.Y, min.Y + SurfaceGeometry.MinSize); break;
                                    case 1: max.X = MathF.Max(pos.X, min.X + SurfaceGeometry.MinSize); break;
                                    case 2: min.Y = MathF.Min(pos.Y, max.Y - SurfaceGeometry.MinSize); break;
                                    default: min.X = MathF.Min(pos.X, max.X - SurfaceGeometry.MinSize); break;
                                }

                                SurfaceGeometry.SetChildBounds(child, min, max);
                                ApplyCropHandling(setup, oldMin, oldMax, min, max);

                                if (guide.HasValue && hasProjection)
                                    DrawSnapGuide(dl, parentProjection, rToView, viewMin, parent, horizontal, guide.Value, edgeParentOrigin);
                            });
        }

        ImGui.PopID();

        DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screen, child.Id, child.Name, isFocused, fade, childPulse);
        HandleLabelMove(setup, dl, rToView, rToOutput, viewMin, outputToSurface, carrier, carrierMapping, parent, child, screen);
    }

    /// <summary>
    /// The label doubles as the region's move handle. The label is a plain draw (no ImGui item), so the grab
    /// is detected by hand — but the lifecycle below is the same snapshot/undo skeleton as every other
    /// rectangle edit (<see cref="RunGesture"/>). Free movement, but a nearly-straight drag snaps flat and
    /// draws the axis it locked to — placing a region level with its neighbours is the common case.
    /// </summary>
    private void HandleLabelMove(Setup setup, ImDrawListPtr dl, Homography rToView, Homography rToOutput, Vector2 viewMin,
                                 Homography outputToSurface, Surface carrier, Surface.OutputMapping carrierMapping,
                                 Surface parent, Surface child, ReadOnlySpan<Vector2> screen)
    {
        if (string.IsNullOrEmpty(child.Name))
            return;

        var phase = CanvasPointHandle.DragPhase.None;
        // Alt at the press pans the content under the region instead of moving the region.
        var panning = _gesture.Is(GestureKinds.ContentPan, child.Id)
                      || (!_gesture.IsLive && ImGui.GetIO().KeyAlt && child.SliceId != Guid.Empty);
        var kind = panning ? GestureKinds.ContentPan : GestureKinds.RegionMove;
        if (_gesture.Is(kind, child.Id))
        {
            phase = ImGui.IsMouseDown(ImGuiMouseButton.Left)
                        ? CanvasPointHandle.DragPhase.Dragging
                        : CanvasPointHandle.DragPhase.Completed;
        }
        else if (!_gesture.IsLive && _labelGrabScreen != null
                 && ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                 // Below the click threshold a press is a selection click, not a grab.
                 && (ImGui.GetMousePos() - _labelGrabScreen.Value).Length() > UserSettings.Config.ClickThreshold
                 && IsPointOverLabel(screen, child.Name, _labelGrabScreen.Value))
        {
            // The press selected this region through the picker; the held button continues into its move.
            _labelGrabScreen = null;
            phase = CanvasPointHandle.DragPhase.Started;
        }
        else if (!_gesture.IsLive
                 && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered())
        {
            var (min, max) = CornerPinHandles.GetCenteredLabelRect(screen, child.Name);
            var mouse = ImGui.GetMousePos();
            if (mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y)
                phase = CanvasPointHandle.DragPhase.Started;
        }

        if (phase == CanvasPointHandle.DragPhase.None)
            return;

        RunGesture(phase, setup, kind, panning ? "Pan content" : "Move region", child,
                   onDragging: () =>
                               {
                                   if (panning)
                                   {
                                       var delta = ToParentSpace(setup, carrier, child, outputToSurface, rToOutput, viewMin) - _gesture.GrabPoint;
                                       if (_gesture.EditsContent)
                                           CropHandling.ApplyPan(setup, _gesture.ContentSliceId, _gesture.ContentUvStart, delta, child.SizeInMeters);
                                   }
                                   else
                                   {
                                       ApplyLabelMove(setup, dl, rToView, rToOutput, viewMin, outputToSurface, carrier, carrierMapping, parent, child);
                                   }
                               },
                   onStarted: () =>
                              {
                                  _gesture.GrabPoint = ToParentSpace(setup, carrier, child, outputToSurface, rToOutput, viewMin);
                                  _childMoveAxis = 0;
                                  if (panning)
                                      BeginContentEdit(setup, child);
                              },
                   onCompleted: () => _childMoveAxis = 0);
    }

    private void ApplyLabelMove(Setup setup, ImDrawListPtr dl, Homography rToView, Homography rToOutput, Vector2 viewMin,
                                Homography outputToSurface, Surface carrier, Surface.OutputMapping carrierMapping,
                                Surface parent, Surface child)
    {
        if (_gesture.Snapshot is not { } start)
            return;

        SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out _, out _, out var parentOrigin);
        var startMin = start.LocalPosition;
        var startMax = startMin + start.Size;
        var delta = ToParentSpace(setup, carrier, child, outputToSurface, rToOutput, viewMin) - _gesture.GrabPoint;
        var snapping = !ImGui.GetIO().KeyShift;

        var hasProjection = SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, SurfaceGeometry.CanvasSizeOf(setup, carrierMapping.OutputId), out var surfaceToOutput);
        var halfSize = (startMax - startMin) * 0.5f;
        var thresholds = hasProjection
                             ? SnapThresholds(surfaceToOutput, rToView, viewMin, parent, parentOrigin, startMin + delta + halfSize)
                             : Vector2.Zero;

        // Nearly-straight drags flatten onto the axis — but only within a constant screen-space budget
        // (~1.5× the snap threshold). A plain direction cone widens with drag distance, and on a long
        // drag it captures from 100px+ away, which reads as violent snapping.
        if (snapping && hasProjection)
        {
            var lockX = MathF.Abs(delta.X) > MathF.Abs(delta.Y) * 4 && MathF.Abs(delta.Y) < thresholds.Y * 1.5f;
            var lockY = MathF.Abs(delta.Y) > MathF.Abs(delta.X) * 4 && MathF.Abs(delta.X) < thresholds.X * 1.5f;
            if (lockX)
                delta.Y = 0;
            else if (lockY)
                delta.X = 0;

            _childMoveAxis = lockX ? 1 : lockY ? 2 : 0;
        }
        else
        {
            _childMoveAxis = 0;
        }

        var newMin = startMin + delta;
        var newMax = startMax + delta;

        float? guideX = null;
        float? guideY = null;

        // Align to the parent's and the siblings' edges and centres — which also makes dropping a region into
        // a corner just land there, since the parent's own edges are candidates.
        if (snapping && hasProjection)
        {
            SurfaceGeometry.CollectSnapCandidates(setup, parent, child.Id, _snapXs, _snapYs);

            Span<float> anchorsX = [newMin.X, (newMin.X + newMax.X) * 0.5f, newMax.X];
            if (SurfaceGeometry.TrySnapOffset(_snapXs, anchorsX, thresholds.X, out var offsetX, out var targetX))
            {
                newMin.X += offsetX;
                newMax.X += offsetX;
                guideX = targetX;
            }

            Span<float> anchorsY = [newMin.Y, (newMin.Y + newMax.Y) * 0.5f, newMax.Y];
            if (SurfaceGeometry.TrySnapOffset(_snapYs, anchorsY, thresholds.Y, out var offsetY, out var targetY))
            {
                newMin.Y += offsetY;
                newMax.Y += offsetY;
                guideY = targetY;
            }
        }

        SurfaceGeometry.SetChildBounds(child, newMin, newMax);

        if (!hasProjection)
            return;

        if (guideX.HasValue)
            DrawSnapGuide(dl, surfaceToOutput, rToView, viewMin, parent, true, guideX.Value, parentOrigin);

        if (guideY.HasValue)
            DrawSnapGuide(dl, surfaceToOutput, rToView, viewMin, parent, false, guideY.Value, parentOrigin);

        if (_childMoveAxis == 0)
            return;

        // The locked movement axis, drawn across the parent so it reads as a guide rather than a stub.
        SurfaceGeometry.ChildBounds(child, out var minNow, out var maxNow);
        var mid = (minNow + maxNow) * 0.5f;
        DrawSnapGuide(dl, surfaceToOutput, rToView, viewMin, parent, _childMoveAxis == 2, _childMoveAxis == 1 ? mid.Y : mid.X, parentOrigin);
    }

    private static Vector2[] RectCorners(Vector2 min, Vector2 max)
    {
        return [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)];
    }

    /// <summary>
    /// Snap distances in the parent's own units per axis, from a fixed screen distance. Measured with short
    /// probes around <paramref name="probeInParent"/> (the dragged item), not across the whole parent:
    /// a rectified or keystoned view scales X and Y differently — a width-derived threshold applied to Y can
    /// catch from far more than the intended 7px — and under perspective the scale varies across the surface,
    /// so only a local measurement feels the same everywhere.
    /// </summary>
    private Vector2 SnapThresholds(Homography surfaceToOutput, Homography rToView, Vector2 viewMin, Surface parent,
                                   Vector2 originInCarrier, Vector2 probeInParent)
    {
        var probe = MathF.Max(MathF.Min(parent.SizeInMeters.X, parent.SizeInMeters.Y) * 0.05f, 0.0001f);
        var origin = ProjectParentPoint(surfaceToOutput, rToView, viewMin, originInCarrier, probeInParent);
        var alongX = ProjectParentPoint(surfaceToOutput, rToView, viewMin, originInCarrier, probeInParent + new Vector2(probe, 0));
        var alongY = ProjectParentPoint(surfaceToOutput, rToView, viewMin, originInCarrier, probeInParent + new Vector2(0, probe));

        var wantedPixels = 7 * T3Ui.UiScaleFactor;
        var pixelsX = Vector2.Distance(origin, alongX);
        var pixelsY = Vector2.Distance(origin, alongY);
        return new Vector2(pixelsX > 0.001f ? probe / pixelsX * wantedPixels : 0f,
                           pixelsY > 0.001f ? probe / pixelsY * wantedPixels : 0f);
    }

    private Vector2 ProjectParentPoint(Homography surfaceToOutput, Homography rToView, Vector2 viewMin,
                                       Vector2 originInCarrier, Vector2 pointInParent)
    {
        return _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(originInCarrier + pointInParent)) - viewMin);
    }

    private void DrawSnapGuide(ImDrawListPtr dl, Homography surfaceToOutput, Homography rToView, Vector2 viewMin,
                               Surface parent, bool vertical, float coordinate, Vector2 originInCarrier)
    {
        // Coordinates are in the parent's space; the projection expects the carrier's, so step across. The
        // guide overshoots the parent by its own size on both ends.
        var size = parent.SizeInMeters;
        SurfaceGeometry.LocalBounds(parent, out var parentMin, out var parentMax);
        var from = originInCarrier + (vertical ? new Vector2(coordinate, parentMin.Y - size.Y) : new Vector2(parentMin.X - size.X, coordinate));
        var to = originInCarrier + (vertical ? new Vector2(coordinate, parentMax.Y + size.Y) : new Vector2(parentMax.X + size.X, coordinate));

        var a = _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(from)) - viewMin);
        var b = _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(to)) - viewMin);
        dl.AddLine(a, b, UiColors.StatusAnimated.Fade(0.6f), 1 * T3Ui.UiScaleFactor);
    }

    /// <summary>
    /// A point (the cursor by default) in the child's <em>immediate parent's</em> space. The inverse only gets
    /// us into the carrier's space, so for a nested region we still have to step down by the parent's origin —
    /// that offset is what makes editing work at any nesting depth.
    /// </summary>
    private Vector2 ToParentSpace(Setup setup, Surface carrier, Surface child, Homography outputToSurface,
                                  Homography rToOutput, Vector2 viewMin, Vector2? viewPoint = null)
    {
        var inView = viewPoint ?? _projection.ScreenToCanvas(ImGui.GetMousePos());
        var inCarrier = outputToSurface.TransformPoint(rToOutput.TransformPoint(inView + viewMin));

        return SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out _, out _, out var parentOrigin)
                   ? inCarrier - parentOrigin
                   : inCarrier;
    }

    /// <summary>Whether the cursor is on a surface's centre label chip — its grab area, which takes priority
    /// over any handle beneath it.</summary>
    private static bool IsMouseOverLabel(ReadOnlySpan<Vector2> screenQuad, string name)
    {
        return IsPointOverLabel(screenQuad, name, ImGui.GetMousePos());
    }

    private static bool IsPointOverLabel(ReadOnlySpan<Vector2> screenQuad, string name, Vector2 screenPoint)
    {
        var (min, max) = CornerPinHandles.GetCenteredLabelRect(screenQuad, name);
        return screenPoint.X >= min.X && screenPoint.X <= max.X && screenPoint.Y >= min.Y && screenPoint.Y <= max.Y;
    }
}
