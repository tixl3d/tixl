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
                             Homography rToView, Homography rToOutput, Vector2 viewMin, Vector2 canvasSize,
                             bool editable, float fade, bool hasContent)
    {
        if (output.Patches.Count == 0 || fade <= 0.01f)
            return;

        var focusedPatchId = selection != null && selection.Targets.Count > 0
                             && selection.Targets[0].Kind == SetupEntitySelection.EntityKind.Patch
                                 ? selection.Targets[0].EntityId
                                 : Guid.Empty;

        Span<Vector2> screen = stackalloc Vector2[4];
        for (var i = 0; i < output.Patches.Count; i++)
        {
            var patch = output.Patches[i];
            // The implicit full-canvas patch sits exactly on the canvas border — drawing it adds an outline and
            // four handles that say nothing. It appears once it is named, moved, or joined by a second patch.
            if (patch.Quad.Length < 4 || SetupRelations.IsImplicitPatch(output, patch))
                continue;

            // Stored as ratios of the canvas; this canvas works in its pixels like the rest of the output view,
            // so the patch is read into a pixel scratch, edited there, and written back below.
            LoadPatchPixels(patch, canvasSize);
            for (var c = 0; c < 4; c++)
            {
                _patchViewQuad[c] = rToView.TransformPoint(_patchPx[c]) - viewMin;
                screen[c] = _projection.CanvasToScreen(_patchViewQuad[c]);
            }

            ImGui.PushID(patch.Id.GetHashCode());

            var label = SetupActions.PatchLabel(output, patch);
            var isFocused = patch.Id == focusedPatchId;
            var isSelected = isFocused || (selection?.IsSelected(SetupEntitySelection.EntityKind.Patch, patch.Id) ?? false);
            var pulse = isSelected ? 0f : FrameStats.GetPulse(patch.Id);

            var style = CornerPinHandles.Style.ForSurface(null, editable, isSelected, fade, hue: SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface));
            style.DrawChecker = !hasContent;
            style.EdgeColor = PulseColor(style.EdgeColor, pulse);

            // Same label-over-handle rule as surfaces: the label is the grab area, so handles under it yield.
            var handleActive = _gesture.HotId == patch.Id && _gesture.Kind is GestureKinds.PatchQuad or GestureKinds.PatchMove;
            var pointerOverLabel = !handleActive && IsMouseOverLabel(screen, label);
            style.Editable = editable && !pointerOverLabel && !_isolate;

            var phase = CornerPinHandles.Draw(_patchViewQuad, _projection, style, out var draggedCorner, out var cornerHovered);
            if (phase != CanvasPointHandle.DragPhase.None)
            {
                for (var c = 0; c < 4; c++)
                    _patchPx[c] = rToOutput.TransformPoint(_patchViewQuad[c] + viewMin);

                if (phase == CanvasPointHandle.DragPhase.Dragging && draggedCorner >= 0 && !ImGui.GetIO().KeyShift)
                {
                    CollectPatchSnapCandidates(output, patch.Id, canvasSize);
                    var threshold = PatchSnapThreshold();
                    ref var corner = ref _patchPx[draggedCorner];
                    Span<float> x = [corner.X];
                    if (SurfaceGeometry.TrySnapOffset(_snapXs, x, threshold, out _, out var targetX))
                        corner.X = targetX;

                    Span<float> y = [corner.Y];
                    if (SurfaceGeometry.TrySnapOffset(_snapYs, y, threshold, out _, out var targetY))
                        corner.Y = targetY;
                }

                StorePatchPixels(patch, canvasSize);
            }

            RunPatchQuadDrag(phase, setup, patch, canvasSize);

            // The label doubles as the move handle — the press selects (through the picker), holding on moves.
            if (phase == CanvasPointHandle.DragPhase.None)
                HandlePatchMove(setup, output, patch, isFocused, editable && !_isolate, label, screen, rToView, rToOutput, viewMin, canvasSize);

            if (cornerHovered || phase != CanvasPointHandle.DragPhase.None)
                FrameStats.PulseItemWithId(patch.Id);

            if (phase == CanvasPointHandle.DragPhase.Started && !_isolate)
                selection?.Select(SetupEntitySelection.EntityKind.Patch, patch.Id);

            // Edge handles for the focused patch only: an edge crops the tile, keeping the opposite edge put.
            if (style.Editable && isFocused)
            {
                var edgePhase = CornerPinHandles.DrawEdgeHandles(_patchViewQuad, _projection, style, out var edge, out var edgePos);
                if (edge >= 0)
                    HandlePatchEdgeDrag(edgePhase, setup, output, patch, edge, edgePos, rToOutput, viewMin, canvasSize);
            }

            ImGui.PopID();
            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Patch, screen, patch.Id, label, isSelected, fade, pulse);
        }
    }

    /// <summary>A patch gesture: the pre-drag quad kept for re-basing, the undo step from the setup snapshot.</summary>
    private void RunPatchQuadDrag(CanvasPointHandle.DragPhase phase, Setup setup, OutputDefinition.Patch patch, Vector2 canvasSize,
                                  bool move = false)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                for (var c = 0; c < 4; c++)
                    _patchOldQuad[c] = patch.Quad[c] * canvasSize;

                BeginGesture(setup, move ? GestureKinds.PatchMove : GestureKinds.PatchQuad, move ? "Move patch" : "Adjust patch", patch.Id,
                             grabPoint: _projection.ScreenToCanvas(ImGui.GetMousePos()));
                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_gesture.HotId == patch.Id)
                    EndGesture(setup);

                break;
        }
    }

    private void HandlePatchMove(Setup setup, OutputDefinition output, OutputDefinition.Patch patch, bool isFocused, bool editable, string label,
                                 ReadOnlySpan<Vector2> screen, Homography rToView, Homography rToOutput, Vector2 viewMin, Vector2 canvasSize)
    {
        var movePhase = CanvasPointHandle.DragPhase.None;
        if (_gesture.Is(GestureKinds.PatchMove, patch.Id))
        {
            movePhase = ImGui.IsMouseDown(ImGuiMouseButton.Left)
                            ? CanvasPointHandle.DragPhase.Dragging
                            : CanvasPointHandle.DragPhase.Completed;
        }
        else if (!_gesture.IsLive && _labelGrabScreen != null && isFocused && editable
                 && ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                 && (ImGui.GetMousePos() - _labelGrabScreen.Value).Length() > UserSettings.Config.ClickThreshold
                 && IsPointOverLabel(screen, label, _labelGrabScreen.Value))
        {
            _labelGrabScreen = null;
            movePhase = CanvasPointHandle.DragPhase.Started;
        }

        switch (movePhase)
        {
            case CanvasPointHandle.DragPhase.Started:
                RunPatchQuadDrag(movePhase, setup, patch, canvasSize, move: true);
                break;

            case CanvasPointHandle.DragPhase.Dragging when _gesture.Is(GestureKinds.PatchMove, patch.Id):
            {
                // Rigid in view space, carried through R per corner — the same rule as a surface move.
                var moveDelta = _projection.ScreenToCanvas(ImGui.GetMousePos()) - _gesture.GrabPoint;
                for (var c = 0; c < 4; c++)
                    _patchPx[c] = rToOutput.TransformPoint(rToView.TransformPoint(_patchOldQuad[c]) + moveDelta);

                if (!ImGui.GetIO().KeyShift)
                {
                    CollectPatchSnapCandidates(output, patch.Id, canvasSize);
                    var threshold = PatchSnapThreshold();
                    QuadBounds(_patchPx, out var min, out var max);
                    Span<float> xs = [min.X, (min.X + max.X) * 0.5f, max.X];
                    Span<float> ys = [min.Y, (min.Y + max.Y) * 0.5f, max.Y];
                    // Move the tile by the offset, then pin the edge that caught to the exact coordinate it
                    // caught on — the shared edge has to be the same float as its neighbour's, not merely close.
                    var offset = Vector2.Zero;
                    var snappedX = SurfaceGeometry.TrySnapOffset(_snapXs, xs, threshold, out var offsetX, out var targetX);
                    if (snappedX)
                        offset.X = offsetX;

                    var snappedY = SurfaceGeometry.TrySnapOffset(_snapYs, ys, threshold, out var offsetY, out var targetY);
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

            case CanvasPointHandle.DragPhase.Completed:
                RunPatchQuadDrag(movePhase, setup, patch, canvasSize);
                break;
        }
    }

    /// <summary>
    /// An edge drag on a patch moves that edge along its normal (a crop for a tile); with Ctrl the edge slides
    /// by the full delta (a shear). Re-based from the pre-drag quad each frame, so the edit doesn't compound.
    /// </summary>
    private void HandlePatchEdgeDrag(CanvasPointHandle.DragPhase phase, Setup setup, OutputDefinition output, OutputDefinition.Patch patch,
                                     int edge, Vector2 viewPos, Homography rToOutput, Vector2 viewMin, Vector2 canvasSize)
    {
        if (phase == CanvasPointHandle.DragPhase.Started)
            RunPatchQuadDrag(phase, setup, patch, canvasSize);

        if (phase == CanvasPointHandle.DragPhase.Dragging && _gesture.Is(GestureKinds.PatchQuad, patch.Id))
        {
            var e0 = edge;
            var e1 = (edge + 1) % 4;
            var pos = rToOutput.TransformPoint(viewPos + viewMin);
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
                if (SurfaceGeometry.TrySnapOffset(horizontal ? _snapYs : _snapXs, coordinate, PatchSnapThreshold(), out _, out var target))
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

        if (phase == CanvasPointHandle.DragPhase.Completed)
            RunPatchQuadDrag(phase, setup, patch, canvasSize);
    }

    /// <summary>Canvas edges and centre plus every other patch's bounds — what a patch edit snaps to, in output px.</summary>
    private void CollectPatchSnapCandidates(OutputDefinition output, Guid excludeId, Vector2 canvasSize)
    {
        _snapXs.Clear();
        _snapYs.Clear();
        _snapXs.Add(0);
        _snapXs.Add(canvasSize.X * 0.5f);
        _snapXs.Add(canvasSize.X);
        _snapYs.Add(0);
        _snapYs.Add(canvasSize.Y * 0.5f);
        _snapYs.Add(canvasSize.Y);

        foreach (var other in output.Patches)
        {
            if (other.Id == excludeId || other.Quad.Length < 4)
                continue;

            QuadBounds(ScaleQuad(other.Quad, canvasSize), out var min, out var max);
            _snapXs.Add(min.X);
            _snapXs.Add((min.X + max.X) * 0.5f);
            _snapXs.Add(max.X);
            _snapYs.Add(min.Y);
            _snapYs.Add((min.Y + max.Y) * 0.5f);
            _snapYs.Add(max.Y);
        }
    }

    /// <summary>A constant screen distance expressed in output pixels at the current zoom.</summary>
    private float PatchSnapThreshold()
    {
        var a = _projection.CanvasToScreen(Vector2.Zero);
        var b = _projection.CanvasToScreen(new Vector2(1, 0));
        var screenPerCanvas = Vector2.Distance(a, b);
        return screenPerCanvas > 0.0001f ? 7 * T3Ui.UiScaleFactor / screenPerCanvas : 0f;
    }

    /// <summary>
    /// After a move snapped an axis, sets whichever of the tile's two edges landed on <paramref name="target"/>
    /// to exactly that value, so it shares a float with the neighbour it caught on rather than merely rounding
    /// to it. Corners that were on that edge move; the opposite edge stays where the offset put it.
    /// </summary>
    private static void PinAxisToTarget(Vector2[] quad, float movedMin, float movedMax, float target, bool horizontal)
    {
        var onMin = MathF.Abs(movedMin - target) <= MathF.Abs(movedMax - target);
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

    // The patch being edited, in canvas pixels. Stored quads are ratios; every edit in this file happens here.
    private static readonly Vector2[] _patchPx = new Vector2[4];
    private static readonly Vector2[] _scaleScratch = new Vector2[4];

    /// <summary>How close two corners' coordinates must be to count as one axis-aligned edge, as a fraction of
    /// the canvas — well under a pixel at any sane resolution.</summary>
    private const float AlignedEpsilon = 0.00002f;

    private static void QuadBounds(Vector2[] quad, out Vector2 min, out Vector2 max)
    {
        min = max = quad[0];
        for (var i = 1; i < quad.Length; i++)
        {
            min = Vector2.Min(min, quad[i]);
            max = Vector2.Max(max, quad[i]);
        }
    }
}
