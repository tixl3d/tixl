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
/// Patch editing on the projector canvas: the direct-pipe rects, their corner/edge drags, moves and snapping.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// The output's patches: rectangles (or keystone quads) of canvas pixels on the direct pipe. Corners drag
    /// freely — a warped patch is a surface-less keystone; the selected patch's edges crop axis-aligned and its
    /// label moves it whole. Every edit snaps to the canvas edges and to the other patches, so tiles butt up.
    /// </summary>
    private void DrawPatches(Setup setup, OutputDefinition output, SetupEntitySelection? selection, ImDrawListPtr dl,
                             Homography rectifiedToView, Homography rectifiedToOutput, Vector2 viewMin, Vector2 canvasSize,
                             bool editable, float fade, bool hasContent)
    {
        if (output.Patches.Count == 0 || fade <= 0.01f)
            return;

        var focusedPatchId = selection != null && selection.Targets.Count > 0
                             && selection.Targets[0].Kind == SetupEntityKinds.Patch
                                 ? selection.Targets[0].EntityId
                                 : Guid.Empty;

        Span<Vector2> screen = stackalloc Vector2[4];
        for (var i = 0; i < output.Patches.Count; i++)
        {
            var patch = output.Patches[i];
            if (patch.Quad.Length < 4)
                continue;

            // The implicit full-canvas patch is folded into the output everywhere else — its outline would only
            // double the canvas border. On the output canvas its corners are still worth offering: dragging one
            // keystones a feed that goes straight to the output, without first having to make a patch by hand.
            // That drag is also what promotes it, since it then no longer covers the full canvas.
            var isImplicit = SetupRelations.IsImplicitPatch(output, patch);
            if (isImplicit && _editMode != EditModes.Output)
                continue;

            // Stored as ratios of the canvas; this canvas works in its pixels like the rest of the output view,
            // so the patch is read into a pixel scratch, edited there, and written back below.
            LoadPatchPixels(patch, canvasSize);
            for (var c = 0; c < 4; c++)
            {
                _patchViewQuad[c] = rectifiedToView.TransformPoint(_patchPx[c]) - viewMin;
                screen[c] = _projection.CanvasToScreen(_patchViewQuad[c]);
            }

            ImGui.PushID(patch.Id.GetHashCode());

            // No label while it is still the output itself: it would sit over the whole canvas and read as a
            // second name for it. The label (and the whole-tile move it carries) appears with the promotion.
            var label = isImplicit ? string.Empty : CachedPatchLabelWithSize(output, patch, canvasSize);
            var isFocused = patch.Id == focusedPatchId;
            var isSelected = isFocused || (selection?.IsSelected(SetupEntityKinds.Patch, patch.Id) ?? false);
            var pulse = isSelected ? 0f : FrameStats.CrossHighlightAmount(patch.Id);

            // A patch is a cut of the canvas, not a surface: it wears the neutral patch hue, never the surface green.
            var style = CornerPinHandles.Style.ForSurface(null, editable, isSelected, fade, hue: SetupColors.ForKind(SetupEntityKinds.Patch));
            style.ShowsChecker = !hasContent;
            style.EdgeColor = PulseColor(style.EdgeColor, pulse);

            // Same label-over-handle rule as surfaces: the label is the grab area, so handles under it yield.
            var handleActive = _gesture.HotId == patch.Id && _gesture.Kind is GestureKinds.PatchQuad or GestureKinds.PatchMove;
            var pointerOverLabel = !handleActive && !isImplicit && IsMouseOverLabel(screen, label);
            // A fitted patch's place and size are derived from its aspect and scale, so it has no handles to drag.
            style.IsEditable = editable && !patch.IsFitted && !pointerOverLabel && !_isolatesFocusedSurface;

            var phase = CornerPinHandles.Draw(_patchViewQuad, _projection, style, out var draggedCorner, out var cornerHovered);
            if (phase != CanvasPointHandle.DragPhases.None)
            {
                for (var c = 0; c < 4; c++)
                    _patchPx[c] = rectifiedToOutput.TransformPoint(_patchViewQuad[c] + viewMin);

                if (phase == CanvasPointHandle.DragPhases.Dragging && draggedCorner >= 0 && !ImGui.GetIO().KeyShift)
                {
                    var threshold = PatchSnapThreshold();
                    ref var corner = ref _patchPx[draggedCorner];

                    // Onto another patch's corner when close, else onto 45° steps from the two neighbouring corners so
                    // edges stay straight, else onto the other patches' edges and the canvas.
                    if (!TrySnapToPatchCorners(output, patch.Id, canvasSize, threshold, ref corner)
                        && !TrySnapCornerAngles(_patchPx, draggedCorner, threshold, ref corner))
                    {
                        CollectPatchSnapCandidates(output, patch.Id, canvasSize);
                        Span<float> x = [corner.X];
                        if (_snapping.TrySnap(RectSnapping.Axes.X, x, threshold, out _, out var targetX))
                            corner.X = targetX;

                        Span<float> y = [corner.Y];
                        if (_snapping.TrySnap(RectSnapping.Axes.Y, y, threshold, out _, out var targetY))
                            corner.Y = targetY;
                    }
                }

                StorePatchPixels(patch, canvasSize);
                if (phase == CanvasPointHandle.DragPhases.Dragging && draggedCorner >= 0)
                {
                    _patchViewQuad[draggedCorner] = rectifiedToView.TransformPoint(_patchPx[draggedCorner]) - viewMin;
                    CanvasPointHandle.ReportSnappedPosition(_projection, _patchViewQuad[draggedCorner]);
                }
            }

            RunPatchQuadDrag(phase, setup, patch, canvasSize);

            // The label doubles as the move handle — the press selects (through the picker), holding on moves.
            if (phase == CanvasPointHandle.DragPhases.None && !isImplicit)
                HandlePatchMove(setup, output, patch, selection, isFocused, editable && !patch.IsFitted && !_isolatesFocusedSurface, label, screen, rectifiedToView, rectifiedToOutput, viewMin, canvasSize);

            if (cornerHovered || phase != CanvasPointHandle.DragPhases.None)
                FrameStats.RequestCrossHighlight(patch.Id);

            // A double-click on the label renames the patch where names are edited: its row in the Flow Outliner.
            if (!isImplicit && pointerOverLabel && selection != null && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                CancelGesture();
                OutlinerItem.BeginRename(selection, SetupEntityKinds.Patch, patch.Id, patch.Name);
            }

            if (phase == CanvasPointHandle.DragPhases.Started && !_isolatesFocusedSurface)
                selection?.Select(SetupEntityKinds.Patch, patch.Id);

            // Edge handles for the focused patch only: an edge crops the tile, keeping the opposite edge put.
            if (style.IsEditable && isFocused)
            {
                var edgePhase = CornerPinHandles.DrawEdgeHandles(_patchViewQuad, _projection, style, out var edge, out var edgePos);
                if (edge >= 0)
                    HandlePatchEdgeDrag(edgePhase, setup, output, patch, edge, edgePos, rectifiedToOutput, viewMin, canvasSize);
            }

            ImGui.PopID();
            if (!isImplicit)
                DrawEntityLabel(dl, SetupEntityKinds.Patch, screen, patch.Id, label, isSelected, fade, pulse, frameColor: style.EdgeColor);

            if (patch.QuarterTurns != 0)
                DrawPictureTopMarker(dl, screen, patch.QuarterTurns, style.EdgeColor.Fade(fade));
        }
    }

    /// <summary>A patch gesture: the pre-drag quad kept for re-basing, the undo step from the setup snapshot.</summary>
    private void RunPatchQuadDrag(CanvasPointHandle.DragPhases phase, Setup setup, OutputDefinition.Patch patch, Vector2 canvasSize,
                                  bool move = false)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                for (var c = 0; c < 4; c++)
                    _patchOldQuad[c] = patch.Quad[c] * canvasSize;

                BeginGesture(setup, move ? GestureKinds.PatchMove : GestureKinds.PatchQuad, move ? "Move patch" : "Adjust patch", patch.Id,
                             grabPoint: _projection.ScreenToCanvas(ImGui.GetMousePos()));
                break;

            case CanvasPointHandle.DragPhases.Completed:
                if (_gesture.HotId == patch.Id)
                    EndGesture(setup);

                break;
        }
    }

    private void HandlePatchMove(Setup setup, OutputDefinition output, OutputDefinition.Patch patch, SetupEntitySelection? selection, bool isFocused, bool editable,
                                 string label, ReadOnlySpan<Vector2> screen, Homography rectifiedToView, Homography rectifiedToOutput, Vector2 viewMin, Vector2 canvasSize)
    {
        var movePhase = CanvasPointHandle.DragPhases.None;
        if (_gesture.Is(GestureKinds.PatchMove, patch.Id))
        {
            movePhase = ImGui.IsMouseDown(ImGuiMouseButton.Left)
                            ? CanvasPointHandle.DragPhases.Dragging
                            : CanvasPointHandle.DragPhases.Completed;
        }
        else if (isFocused && editable && TryTakeLabelGrab(screen, label))
        {
            movePhase = CanvasPointHandle.DragPhases.Started;
        }

        switch (movePhase)
        {
            case CanvasPointHandle.DragPhases.Started:
                RunPatchQuadDrag(movePhase, setup, patch, canvasSize, move: true);

                // Alt: a copy takes the drag, the original stays; copied inside the snapshot, so it undoes as one.
                if (ImGui.GetIO().KeyAlt && selection != null)
                {
                    SetupActions.DuplicateEntityInternal(selection, setup, SetupEntityKinds.Patch, patch.Id);
                    if (selection.TryResolve(setup, out var copyKind, out var copyId) && copyKind == SetupEntityKinds.Patch && copyId != patch.Id)
                    {
                        _gesture.HotId = copyId;
                        _gesture.Name = "Duplicate patch";
                    }
                }

                break;

            case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.PatchMove, patch.Id):
            {
                // Rigid in view space, carried through R per corner — the same rule as a surface move.
                var moveDelta = _projection.ScreenToCanvas(ImGui.GetMousePos()) - _gesture.GrabPoint;
                for (var c = 0; c < 4; c++)
                    _patchPx[c] = rectifiedToOutput.TransformPoint(rectifiedToView.TransformPoint(_patchOldQuad[c]) + moveDelta);

                if (!ImGui.GetIO().KeyShift)
                {
                    CollectPatchSnapCandidates(output, patch.Id, canvasSize);
                    var threshold = PatchSnapThreshold();
                    CanvasDraw.Bounds(_patchPx, out var min, out var max);
                    Span<float> xs = [min.X, (min.X + max.X) * 0.5f, max.X];
                    Span<float> ys = [min.Y, (min.Y + max.Y) * 0.5f, max.Y];
                    // Move the tile by the offset, then pin the edge that caught to the exact coordinate it
                    // caught on — the shared edge has to be the same float as its neighbour's, not merely close.
                    var offset = Vector2.Zero;
                    var snappedX = _snapping.TrySnap(RectSnapping.Axes.X, xs, threshold, out var offsetX, out var targetX);
                    if (snappedX)
                        offset.X = offsetX;

                    var snappedY = _snapping.TrySnap(RectSnapping.Axes.Y, ys, threshold, out var offsetY, out var targetY);
                    if (snappedY)
                        offset.Y = offsetY;

                    for (var c = 0; c < 4; c++)
                        _patchPx[c] += offset;

                    if (snappedX)
                        PinAxisToTarget(_patchPx, min.X + offset.X, max.X + offset.X, targetX, horizontal: false);

                    if (snappedY)
                        PinAxisToTarget(_patchPx, min.Y + offset.Y, max.Y + offset.Y, targetY, horizontal: true);
                }

                StorePatchPixels(patch, canvasSize);
                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                RunPatchQuadDrag(movePhase, setup, patch, canvasSize);
                break;
        }
    }

    /// <summary>
    /// An edge drag on a patch moves that edge along its normal (a crop for a tile); with Ctrl the edge slides
    /// by the full delta (a shear). Re-based from the pre-drag quad each frame, so the edit doesn't compound.
    /// </summary>
    private void HandlePatchEdgeDrag(CanvasPointHandle.DragPhases phase, Setup setup, OutputDefinition output, OutputDefinition.Patch patch,
                                     int edge, Vector2 viewPos, Homography rectifiedToOutput, Vector2 viewMin, Vector2 canvasSize)
    {
        if (phase == CanvasPointHandle.DragPhases.Started)
            RunPatchQuadDrag(phase, setup, patch, canvasSize);

        if (phase == CanvasPointHandle.DragPhases.Dragging && _gesture.Is(GestureKinds.PatchQuad, patch.Id))
        {
            var e0 = edge;
            var e1 = (edge + 1) % 4;
            var pos = rectifiedToOutput.TransformPoint(viewPos + viewMin);
            var midpoint = (_patchOldQuad[e0] + _patchOldQuad[e1]) * 0.5f;
            var delta = pos - midpoint;

            if (!ImGui.GetIO().KeyCtrl)
            {
                var along = _patchOldQuad[e1] - _patchOldQuad[e0];
                var normal = new Vector2(-along.Y, along.X);
                if (normal.LengthSquared() > 0.0001f)
                {
                    normal = Vector2.Normalize(normal);
                    delta = normal * Vector2.Dot(delta, normal);
                }
            }

            Array.Copy(_patchOldQuad, _patchPx, 4);
            _patchPx[e0] += delta;
            _patchPx[e1] += delta;

            // An axis-aligned edge snaps its coordinate to the canvas edges and the neighbouring tiles.
            var horizontal = edge is 0 or 2;
            var aligned = horizontal
                              ? MathF.Abs(_patchPx[e0].Y - _patchPx[e1].Y) < AlignedEpsilon * canvasSize.Y
                              : MathF.Abs(_patchPx[e0].X - _patchPx[e1].X) < AlignedEpsilon * canvasSize.X;
            if (aligned && !ImGui.GetIO().KeyShift)
            {
                CollectPatchSnapCandidates(output, patch.Id, canvasSize);
                Span<float> coordinate = [horizontal ? _patchPx[e0].Y : _patchPx[e0].X];
                if (_snapping.TrySnap(horizontal ? RectSnapping.Axes.Y : RectSnapping.Axes.X, coordinate, PatchSnapThreshold(), out _, out var target))
                {
                    // Assigned, not offset: `pos + (target - pos)` can land a bit short of target, and two tiles
                    // whose shared edge differs in the last bit either double a row of pixels or leave a gap.
                    // Identical floats are what lets the rasterizer's top-left fill rule tile them seamlessly.
                    if (horizontal)
                    {
                        _patchPx[e0].Y = target;
                        _patchPx[e1].Y = target;
                    }
                    else
                    {
                        _patchPx[e0].X = target;
                        _patchPx[e1].X = target;
                    }
                }
            }

            StorePatchPixels(patch, canvasSize);
        }

        if (phase == CanvasPointHandle.DragPhases.Completed)
            RunPatchQuadDrag(phase, setup, patch, canvasSize);
    }

    /// <summary>Canvas edges and centre plus every other patch's bounds — what a patch edit snaps to, in output px.</summary>
    private void CollectPatchSnapCandidates(OutputDefinition output, Guid excludeId, Vector2 canvasSize)
    {
        _snapping.Clear();
        _snapping.AddRectEdgesAndCentre(Vector2.Zero, canvasSize);

        foreach (var other in output.Patches)
        {
            if (other.Id == excludeId || other.Quad.Length < 4)
                continue;

            CanvasDraw.Bounds(ScaleQuad(other.Quad, canvasSize), out var min, out var max);
            _snapping.AddRectEdgesAndCentre(min, max);
        }
    }

    /// <summary>A corner near another patch's corner (or the canvas') lands exactly on it.</summary>
    private static bool TrySnapToPatchCorners(OutputDefinition output, Guid excludeId, Vector2 canvasSize, float threshold, ref Vector2 corner)
    {
        var best = threshold;
        var found = false;
        var snapped = corner;
        Span<Vector2> canvasCorners = [Vector2.Zero, new Vector2(canvasSize.X, 0), canvasSize, new Vector2(0, canvasSize.Y)];
        for (var c = 0; c < 4; c++)
            Consider(canvasCorners[c], ref best, ref snapped, ref found, corner);

        foreach (var other in output.Patches)
        {
            if (other.Id == excludeId || other.Quad.Length < 4)
                continue;

            for (var c = 0; c < 4; c++)
                Consider(other.Quad[c] * canvasSize, ref best, ref snapped, ref found, corner);
        }

        corner = snapped;
        return found;

        static void Consider(Vector2 candidate, ref float best, ref Vector2 snapped, ref bool found, Vector2 corner)
        {
            var distance = (candidate - corner).Length();
            if (distance >= best)
                return;

            best = distance;
            snapped = candidate;
            found = true;
        }
    }

    /// <summary>
    /// Keeps the edges meeting at a dragged corner on 45° steps: each neighbouring corner whose 45° ray passes
    /// within <paramref name="threshold"/> of the corner (a fixed screen distance, whatever the edge's length or
    /// the zoom) pulls the corner onto that ray; with both pulling, the corner goes where the two rays cross.
    /// False when neither ray is near.
    /// </summary>
    private static bool TrySnapCornerAngles(Vector2[] quad, int draggedCorner, float threshold, ref Vector2 corner)
    {
        const float step = MathF.PI / 4;
        var previous = quad[(draggedCorner + 3) % 4];
        var next = quad[(draggedCorner + 1) % 4];
        var hasPrevious = TrySnappedRay(previous, corner, threshold, out var rayPrevious);
        var hasNext = TrySnappedRay(next, corner, threshold, out var rayNext);
        if (!hasPrevious && !hasNext)
            return false;

        if (hasPrevious && hasNext)
        {
            var cross = rayPrevious.X * rayNext.Y - rayPrevious.Y * rayNext.X;
            if (MathF.Abs(cross) > 0.01f)
            {
                // previous + t·rayPrevious = next + u·rayNext, solved for t.
                var between = next - previous;
                var t = (between.X * rayNext.Y - between.Y * rayNext.X) / cross;
                corner = previous + rayPrevious * t;
                return true;
            }
        }

        var from = hasPrevious ? previous : next;
        var ray = hasPrevious ? rayPrevious : rayNext;
        corner = from + ray * Vector2.Dot(corner - from, ray);
        return true;

        static bool TrySnappedRay(Vector2 from, Vector2 to, float threshold, out Vector2 ray)
        {
            ray = Vector2.Zero;
            var edge = to - from;
            if (edge.LengthSquared() < 0.0001f)
                return false;

            var angle = MathF.Atan2(edge.Y, edge.X);
            var snapped = MathF.Round(angle / step) * step;
            ray = new Vector2(MathF.Cos(snapped), MathF.Sin(snapped));

            // The corner's distance from the ray, not the angle: the same few pixels for a short and a long edge.
            var offRay = MathF.Abs(edge.X * ray.Y - edge.Y * ray.X);
            return offRay <= threshold;
        }
    }

    /// <summary>A constant screen distance expressed in output pixels at the current zoom.</summary>
    private float PatchSnapThreshold()
    {
        return RectSnapping.ThresholdFor(_projection, Vector2.Zero, 1f).X;
    }

    /// <summary>
    /// After a move snapped an axis, sets whichever of the tile's two edges landed on <paramref name="target"/>
    /// to exactly that value, so it shares a float with the neighbour it caught on rather than merely rounding
    /// to it. Corners that were on that edge move; the opposite edge stays where the offset put it.
    /// </summary>
    private static void PinAxisToTarget(Vector2[] quad, float movedMin, float movedMax, float target, bool horizontal)
    {
        // Only an edge that actually landed on the target is pinned. When the tile's centre was what caught the
        // line, neither edge is there, and pulling one onto it would resize the tile instead of placing it.
        var onMin = MathF.Abs(movedMin - target) <= AlignedEpsilon * 100;
        var onMax = MathF.Abs(movedMax - target) <= AlignedEpsilon * 100;
        if (!onMin && !onMax)
            return;

        var edgeValue = onMin ? movedMin : movedMax;
        for (var i = 0; i < 4; i++)
        {
            var coordinate = horizontal ? quad[i].Y : quad[i].X;
            if (MathF.Abs(coordinate - edgeValue) > AlignedEpsilon)
                continue;

            if (horizontal)
                quad[i].Y = target;
            else
                quad[i].X = target;
        }
    }

    private void LoadPatchPixels(OutputDefinition.Patch patch, Vector2 canvasSize)
    {
        for (var c = 0; c < 4; c++)
            _patchPx[c] = patch.Quad[c] * canvasSize;
    }

    private static void StorePatchPixels(OutputDefinition.Patch patch, Vector2 canvasSize)
    {
        for (var c = 0; c < 4; c++)
            patch.Quad[c] = _patchPx[c] / canvasSize;
    }

    /// <summary>A stored 0..1 quad in canvas pixels; the scratch is reused, so consume it before the next call.</summary>
    private static Vector2[] ScaleQuad(Vector2[] quad, Vector2 canvasSize)
    {
        for (var c = 0; c < 4; c++)
            _scaleScratch[c] = quad[c] * canvasSize;

        return _scaleScratch;
    }

    /// <summary>
    /// A small wedge on the edge the picture's top now faces, pointing out of the patch. A turned patch
    /// otherwise looks like any other on the canvas — the composite shows the picture sideways, but not which
    /// way is meant to be up, and that is the thing to check against the panel on the wall.
    /// </summary>
    private static void DrawPictureTopMarker(ImDrawListPtr dl, ReadOnlySpan<Vector2> screen, int quarterTurns, T3.Core.DataTypes.Vector.Color color)
    {
        // One turn clockwise sends the source's top edge (TL→TR) to the quad's right edge (TR→BR): edge n.
        var edge = OutputDefinition.Patch.NormalizeTurns(quarterTurns);
        var a = screen[edge];
        var b = screen[(edge + 1) % 4];
        var centre = (screen[0] + screen[1] + screen[2] + screen[3]) * 0.25f;
        var midpoint = (a + b) * 0.5f;

        var outward = midpoint - centre;
        var length = outward.Length();
        if (length < 1f)
            return;

        // An arrow just inside the quad pointing at that edge — clear of the edge handle sitting on the midpoint,
        // and big enough to read at a glance which way the picture stands.
        outward /= length;
        var along = Vector2.Normalize(b - a);
        var scale = T3Ui.UiScaleFactor;
        var size = 14f * scale;
        var tip = midpoint - outward * 8f * scale;
        var baseCentre = tip - outward * size;
        var left = baseCentre + along * size * 0.6f;
        var right = baseCentre - along * size * 0.6f;
        dl.AddTriangleFilled(tip, left, right, color);
        dl.AddTriangle(tip, left, right, UiColors.BackgroundFull.Fade(0.6f), 1f * scale);
    }

    /// <summary>How close two corners' coordinates must be to count as one axis-aligned edge, as a fraction of
    /// the canvas — well under a pixel at any sane resolution.</summary>
    private const float AlignedEpsilon = 0.00002f;

    // Patch edit scratch: the patch being edited in canvas pixels (stored quads are ratios; every edit in this
    // file happens there), a stored quad scaled to pixels, the quad in view space (reused per patch), and the
    // pre-drag quad a re-based edit starts from.
    private static readonly Vector2[] _patchPx = new Vector2[4];
    private static readonly Vector2[] _scaleScratch = new Vector2[4];
    private readonly Vector2[] _patchViewQuad = new Vector2[4];
    private readonly Vector2[] _patchOldQuad = new Vector2[4];
}
