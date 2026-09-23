#nullable enable
using System.Collections.Generic;
using ImGuiNET;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Slice editing for <see cref="SetupOutputView"/>: the axis-aligned rect editor (edges crop, corners scale
/// with the aspect held, the label moves it, everything snapping to the source's own borders), plus what a
/// slice says about where it goes. Slices are edited on their content card on the Board, whose thumbnail is
/// already the source seen flat — no perspective, so no separate canvas for them.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>Lights the rows of everything showing this slice — the answer to "where does this go?" on hover.</summary>
    private static void PulseConsumers(Setup setup, Guid sliceId)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                FrameStats.RequestCrossHighlight(surface.Id);
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId == sliceId)
                    FrameStats.RequestCrossHighlight(patch.Id);
            }
        }
    }

    /// <summary>A small line under the slice's label naming what shows it ("→ Left wall, Patch 2"), or "unused".</summary>
    private void DrawSliceConsumers(ImDrawListPtr dl, Setup setup, Guid sliceId, (Vector2 Min, Vector2 Max) labelRect, float alpha)
    {
        var text = ConsumerLabel(setup, sliceId);
        var scale = T3Ui.UiScaleFactor;
        var pos = new Vector2(labelRect.Min.X, labelRect.Max.Y + 2 * scale);
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, pos, UiColors.TextMuted.Fade(alpha), text);
    }

    /// <summary>Consumer names per slice, rebuilt only when the setup's structure changes — labels are per frame otherwise.</summary>
    private string ConsumerLabel(Setup setup, Guid sliceId)
    {
        if (_consumerLabelsVersion != OutputSetupHandling.StructureVersion)
        {
            _consumerLabels.Clear();
            _consumerLabelsVersion = OutputSetupHandling.StructureVersion;
        }

        if (_consumerLabels.TryGetValue(sliceId, out var cached))
            return cached;

        _consumerText.Clear();
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId != sliceId)
                continue;

            _consumerText.Append(_consumerText.Length == 0 ? "→ " : ", ").Append(surface.Name);
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId != sliceId)
                    continue;

                _consumerText.Append(_consumerText.Length == 0 ? "→ " : ", ").Append(SetupLabels.PatchLabel(output, patch));
            }
        }

        var label = _consumerText.Length == 0 ? "unused" : _consumerText.ToString();
        _consumerLabels[sliceId] = label;
        return label;
    }

    /// <summary>
    /// The slice as an editable rect on its source, in plain canvas space: edges reshape, corners scale with
    /// the aspect held, the middle moves it, everything snapping to the source's borders and midlines. Shared
    /// by the surface's Content view and the source view, which differ only in framing.
    /// </summary>
    private void EditSlice(Setup setup, ImDrawListPtr dl, Slice slice, Vector4 uv,
                           Vector2 sourceOrigin, Vector2 sourceSize, Guid targetId, bool dimOutside)
    {
        if (sourceSize.X <= 0.0001f || sourceSize.Y <= 0.0001f)
            return;

        var min = _projection.CanvasToScreen(sourceOrigin + new Vector2(uv.X, uv.Y) * sourceSize);
        var max = _projection.CanvasToScreen(sourceOrigin + new Vector2(uv.Z, uv.W) * sourceSize);
        SetupStrokes.SnapRect(ref min, ref max);

        // Dim the source outside the slice — this view exists to judge one surface's crop, so everything the
        // surface won't show recedes. (The atlas view, where every slice matters equally, won't do this.)
        if (dimOutside)
        {
            var sourceMin = SetupStrokes.Snap(_projection.CanvasToScreen(sourceOrigin));
            var sourceMax = SetupStrokes.Snap(_projection.CanvasToScreen(sourceOrigin + sourceSize));
            CanvasDraw.ScrimOutside(dl, sourceMin, sourceMax, min, max, UiColors.BackgroundFull.Fade(0.3f));
        }

        // No tint inside: the crop now reads from the dimmed surround, and colouring the content would work
        // against judging it.
        SetupStrokes.DrawInlineRect(dl, min, max, SetupColors.ForKind(SetupEntityKinds.Slice), isSelected: true);


        // Canvas space, like every other handle — the projection subtracts the framing origin itself.
        var sliceMin = sourceOrigin + new Vector2(uv.X, uv.Y) * sourceSize;
        var sliceMax = sourceOrigin + new Vector2(uv.Z, uv.W) * sourceSize;
        _sliceQuadBuffer[0] = sliceMin;
        _sliceQuadBuffer[1] = new Vector2(sliceMax.X, sliceMin.Y);
        _sliceQuadBuffer[2] = sliceMax;
        _sliceQuadBuffer[3] = new Vector2(sliceMin.X, sliceMax.Y);

        ImGui.PushID("slice");
        var style = CornerPinHandles.Style.ForSurface(null, editable: true, selected: true, hue: SetupColors.ForKind(SetupEntityKinds.Surface));
        var edgePhase = CornerPinHandles.DrawEdgeHandles(_sliceQuadBuffer, _projection, style, out var edge, out var edgePos);

        // The slice's name label doubles as its move handle, the same as a surface — so there's no separate
        // centre dot, and the selected slice reads its name on the canvas like everything else.
        Span<Vector2> labelCorners = stackalloc Vector2[4];
        labelCorners[0] = min;
        labelCorners[1] = new Vector2(max.X, min.Y);
        labelCorners[2] = max;
        labelCorners[3] = new Vector2(min.X, max.Y);
        var sliceName = CachedSliceLabel(setup, slice);
        DrawEntityLabel(dl, SetupEntityKinds.Slice, labelCorners, slice.Id, sliceName, isSelected: true, emphasis: 1f);
        DrawSliceConsumers(dl, setup, slice.Id, CornerPinHandles.GetCenteredLabelRect(labelCorners, sliceName), 0.9f);
        var mousePos = ImGui.GetMousePos();
        if (ImGui.IsWindowHovered() && CanvasDraw.Contains(min, max, mousePos))
            PulseConsumers(setup, slice.Id);

        // Move is detected by hand rather than an InvisibleButton, so the label stays a plain draw and the
        // frame-label pick pass (which selects and opens the context menu) isn't blocked by a hovered item.
        var (labelMin, labelMax) = CornerPinHandles.GetCenteredLabelRect(labelCorners, sliceName);
        var overLabel = CanvasDraw.Contains(labelMin, labelMax, mousePos);
        if (overLabel && !ImGui.IsAnyItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var movePhase = CanvasPointHandle.DragPhases.None;
        if (_sliceLabelDragging)
        {
            movePhase = ImGui.IsMouseDown(ImGuiMouseButton.Left) ? CanvasPointHandle.DragPhases.Dragging
                                                                 : CanvasPointHandle.DragPhases.Completed;
            if (movePhase == CanvasPointHandle.DragPhases.Completed)
                _sliceLabelDragging = false;
        }
        else if (overLabel && !ImGui.IsAnyItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _sliceLabelDragging = true;
            movePhase = CanvasPointHandle.DragPhases.Started;
        }

        var centreInCanvas = _projection.ScreenToCanvas(mousePos);
        ImGui.PopID();

        // The snap distance in UV — per axis, since UV is normalized: on a non-square source the same few
        // pixels are a different UV distance horizontally than vertically.
        var thresholds = RectSnapping.ThresholdFor(_projection, sourceOrigin, 1f) / Vector2.Max(sourceSize, new Vector2(0.0001f));
        var thresholdX = thresholds.X;
        var thresholdY = thresholds.Y;
        var snapping = !ImGui.GetIO().KeyShift;

        if (edge >= 0 && edgePhase is not CanvasPointHandle.DragPhases.None)
        {
            // Capture the pre-drag rect before this frame's apply; the commit runs after it (below).
            if (edgePhase == CanvasPointHandle.DragPhases.Started)
                RunSliceDrag(edgePhase, setup, slice);

            var inSource = (edgePos - sourceOrigin) / sourceSize;
            var next = uv;
            switch (edge)
            {
                case 0: next.Y = MathF.Min(inSource.Y, uv.W - SurfaceGeometry.MinSliceSize); break;
                case 1: next.Z = MathF.Max(inSource.X, uv.X + SurfaceGeometry.MinSliceSize); break;
                case 2: next.W = MathF.Max(inSource.Y, uv.Y + SurfaceGeometry.MinSliceSize); break;
                default: next.X = MathF.Min(inSource.X, uv.Z - SurfaceGeometry.MinSliceSize); break;
            }

            // Snap to the source's bounds/midlines and the sibling slices' edges and centres — the same
            // vocabulary a surface edge snaps to (parent + siblings), so the two feel alike.
            if (snapping)
            {
                CollectSliceSnapCandidates(setup, slice.SourceId, slice.Id);
                var movesX = edge is 1 or 3;
                Span<float> anchor = [edge switch { 0 => next.Y, 1 => next.Z, 2 => next.W, _ => next.X }];
                if (_snapping.TrySnap(movesX ? RectSnapping.Axes.X : RectSnapping.Axes.Y, anchor, movesX ? thresholdX : thresholdY,
                                      out _, out var snapTarget))
                {
                    // Assigned, not offset: two slices meeting on an edge have to store the identical value, or
                    // the sampled source rows on either side overlap by one or skip one.
                    switch (edge)
                    {
                        case 0: next.Y = snapTarget; break;
                        case 1: next.Z = snapTarget; break;
                        case 2: next.W = snapTarget; break;
                        default: next.X = snapTarget; break;
                    }

                    DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, movesX, snapTarget);
                }
            }

            // A plain field write per frame; the undo step and save happen once, on Completed (RunSliceDrag).
            slice.SetUvRect(next);
            if (edgePhase != CanvasPointHandle.DragPhases.Started)
                RunSliceDrag(edgePhase, setup, slice);

            return;
        }

        // Corners resize about the opposite corner and hold the slice's aspect, so a slice cut to a surface's
        // shape keeps that shape while it's scaled.
        ImGui.PushID("sliceCorners");
        var cornerStyle = CanvasPointHandle.Style.Default(style.HandleColor);
        cornerStyle.OutlineColor = style.HandleOutlineColor;

        var cornerPhase = CanvasPointHandle.DragPhases.None;
        var draggedCorner = -1;
        var cornerPos = Vector2.Zero;
        for (var i = 0; i < 4; i++)
        {
            ImGui.PushID(i);
            var point = _sliceQuadBuffer[i];
            var phase = CanvasPointHandle.Draw(ref point, _projection, cornerStyle);
            if (phase is not CanvasPointHandle.DragPhases.None)
            {
                cornerPhase = phase;
                draggedCorner = i;
                cornerPos = point;
            }

            ImGui.PopID();
        }

        ImGui.PopID();

        if (draggedCorner >= 0 && cornerPhase is not CanvasPointHandle.DragPhases.None)
        {
            // Capture the pre-drag rect before this frame's apply; the commit runs after it (below).
            if (cornerPhase == CanvasPointHandle.DragPhases.Started)
                RunSliceDrag(cornerPhase, setup, slice);

            var dragged = (cornerPos - sourceOrigin) / sourceSize;
            var currentWidth = MathF.Max(uv.Z - uv.X, 0.0001f);
            var currentHeight = MathF.Max(uv.W - uv.Y, 0.0001f);

            // Corners are TL, TR, BR, BL — each scales away from the one diagonally across.
            var fixedCorner = draggedCorner switch
                                  {
                                      0 => new Vector2(uv.Z, uv.W),
                                      1 => new Vector2(uv.X, uv.W),
                                      2 => new Vector2(uv.X, uv.Y),
                                      _ => new Vector2(uv.Z, uv.Y),
                                  };

            var scale = MathF.Max(MathF.Abs(dragged.X - fixedCorner.X) / currentWidth,
                                  MathF.Abs(dragged.Y - fixedCorner.Y) / currentHeight);
            var width = MathF.Max(currentWidth * scale, SurfaceGeometry.MinSliceSize);
            var height = MathF.Max(currentHeight * scale, SurfaceGeometry.MinSliceSize);

            var moved = fixedCorner + new Vector2(draggedCorner is 1 or 2 ? width : -width,
                                                  draggedCorner is 2 or 3 ? height : -height);
            var cornerMin = Vector2.Min(fixedCorner, moved);
            var cornerMax = Vector2.Max(fixedCorner, moved);
            slice.SetUvRect(new Vector4(cornerMin.X, cornerMin.Y, cornerMax.X, cornerMax.Y));
            if (cornerPhase != CanvasPointHandle.DragPhases.Started)
                RunSliceDrag(cornerPhase, setup, slice);

            return;
        }

        // The atlas (no target) reaches its slice menus through the frame-label pick pass instead, so only the
        // surface-content view (which can "match target aspect") opens the in-rect menu here.
        if (targetId != Guid.Empty)
            DrawSliceMenu(setup, targetId, slice, uv, min, max);

        var cursorUv = (centreInCanvas - sourceOrigin) / sourceSize;
        RunSliceDrag(movePhase, setup, slice);
        switch (movePhase)
        {
            case CanvasPointHandle.DragPhases.Started:
                // Recomputed from this snapshot each frame, so the move can't accumulate drift.
                _sliceMoveStart = (cursorUv, uv);
                break;

            case CanvasPointHandle.DragPhases.Dragging when _sliceMoveStart != null:
            {
                var (origin, startUv) = _sliceMoveStart.Value;
                var size = new Vector2(startUv.Z - startUv.X, startUv.W - startUv.Y);
                var delta = cursorUv - origin;

                // Same axis lock as a region move: directional, but capped at a constant screen budget so
                // it can't widen with drag distance.
                var axisLock = snapping ? RectSnapping.LockAxis(ref delta, thresholds) : RectSnapping.AxisLocks.None;
                var sliceOrigin = new Vector2(startUv.X, startUv.Y) + delta;

                if (snapping)
                {
                    // Either edge, or the centre, may catch — whichever is closest wins per axis. Candidates
                    // are the source bounds plus the sibling slices, same as a surface snapping to siblings.
                    CollectSliceSnapCandidates(setup, slice.SourceId, slice.Id);
                    Span<float> xs = [sliceOrigin.X, sliceOrigin.X + size.X * 0.5f, sliceOrigin.X + size.X];
                    Span<float> ys = [sliceOrigin.Y, sliceOrigin.Y + size.Y * 0.5f, sliceOrigin.Y + size.Y];
                    if (_snapping.TrySnap(RectSnapping.Axes.X, xs, thresholdX, out var offsetX, out var targetX))
                    {
                        sliceOrigin.X += offsetX;
                        DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, vertical: true, targetX);
                    }

                    if (_snapping.TrySnap(RectSnapping.Axes.Y, ys, thresholdY, out var offsetY, out var targetY))
                    {
                        sliceOrigin.Y += offsetY;
                        DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, vertical: false, targetY);
                    }

                    // The locked movement axis, drawn across the source so it reads as a guide (surface parity).
                    if (axisLock == RectSnapping.AxisLocks.X)
                        DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, vertical: false, sliceOrigin.Y + size.Y * 0.5f);
                    else if (axisLock == RectSnapping.AxisLocks.Y)
                        DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, vertical: true, sliceOrigin.X + size.X * 0.5f);
                }

                sliceOrigin.X = Math.Clamp(sliceOrigin.X, 0, MathF.Max(1 - size.X, 0));
                sliceOrigin.Y = Math.Clamp(sliceOrigin.Y, 0, MathF.Max(1 - size.Y, 0));
                slice.SetUvRect(new Vector4(sliceOrigin.X, sliceOrigin.Y, sliceOrigin.X + size.X, sliceOrigin.Y + size.Y));
                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                _sliceMoveStart = null;
                break;
        }
    }

    /// <summary>
    /// Right-click inside a slice (on release, so it doesn't fight right-drag panning) for actions that are
    /// awkward by hand — chiefly cutting it to the shape of the surface it feeds, so the content lands
    /// undistorted.
    /// </summary>
    private void DrawSliceMenu(Setup setup, Guid targetId, Slice slice, Vector4 uv, Vector2 min, Vector2 max)
    {
        var inside = CanvasDraw.Contains(min, max, ImGui.GetMousePos());
        var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
        if (inside && !wasDraggingRight && ImGui.IsMouseReleased(ImGuiMouseButton.Right) && !ImGui.IsAnyItemHovered())
            ImGui.OpenPopup(SliceMenuId);

        if (!SetupPopup.Begin(SliceMenuId))
            return;

        if (CustomComponents.DrawMenuItem(1, "Match target aspect"))
            MatchSliceToTargetAspect(setup, targetId, slice, uv);

        SetupPopup.End();
    }

    /// <summary>
    /// Reshapes the slice so its pixels have the same aspect as the surface it feeds — keeping its centre, and
    /// shrinking to fit if the new shape would run off the source.
    /// </summary>
    private void MatchSliceToTargetAspect(Setup setup, Guid targetId, Slice slice, Vector4 uv)
    {
        var surface = setup.FindSurface(targetId);
        if (surface == null || !OutputContentResolver.TryGetSurfaceSlice(targetId, out _, out var sourceTexture, out _)
            || sourceTexture is not { IsDisposed: false })
            return;

        var surfaceAspect = surface.SizeInMeters.X / MathF.Max(surface.SizeInMeters.Y, 0.0001f);
        var textureWidth = MathF.Max(sourceTexture.Description.Width, 1);
        var textureHeight = MathF.Max(sourceTexture.Description.Height, 1);

        // Want (width·texW)/(height·texH) == surfaceAspect; keep the width and solve for the height.
        var width = MathF.Max(uv.Z - uv.X, SurfaceGeometry.MinSliceSize);
        var height = width * textureWidth / (textureHeight * MathF.Max(surfaceAspect, 0.0001f));

        var fit = MathF.Min(1f, MathF.Min(1f / MathF.Max(width, 0.0001f), 1f / MathF.Max(height, 0.0001f)));
        width *= fit;
        height *= fit;

        var centreX = (uv.X + uv.Z) * 0.5f;
        var centreY = (uv.Y + uv.W) * 0.5f;
        var minX = Math.Clamp(centreX - width * 0.5f, 0, MathF.Max(1 - width, 0));
        var minY = Math.Clamp(centreY - height * 0.5f, 0, MathF.Max(1 - height, 0));

        var rect = new Vector4(minX, minY, minX + width, minY + height);
        SetupUndo.RunUndoable("Adjust slice", setup, () => slice.SetUvRect(rect));
    }

    /// <summary>The one drag lifecycle for slice-rect edits (edge crop, corner scale, label move): a gesture like every other.</summary>
    private void RunSliceDrag(CanvasPointHandle.DragPhases phase, Setup setup, Slice slice)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                BeginGesture(setup, GestureKinds.Slice, "Edit slice", slice.Id);
                break;

            case CanvasPointHandle.DragPhases.Completed:
                if (_gesture.Is(GestureKinds.Slice, slice.Id))
                    EndGesture(setup);

                break;
        }
    }

    /// <summary>Snap targets for slice edits, in source UV: the source's bounds and midlines plus every
    /// sibling slice's edges and centres — the same vocabulary a surface edit snaps to (parent + siblings).</summary>
    private void CollectSliceSnapCandidates(Setup setup, Guid sourceId, Guid excludeSliceId)
    {
        _snapping.Clear();
        _snapping.AddRectEdgesAndCentre(Vector2.Zero, Vector2.One);

        foreach (var other in setup.Slices)
        {
            if (other.Id == excludeSliceId || other.SourceId != sourceId)
                continue;

            var rect = other.UvRect;
            _snapping.AddRectEdgesAndCentre(new Vector2(rect.X, rect.Y), new Vector2(rect.Z, rect.W));
        }
    }

    /// <summary>A caught snap line across the source (extended past its bounds, matching the surface guides).</summary>
    private void DrawSliceSnapGuide(ImDrawListPtr dl, Vector2 sourceOrigin, Vector2 sourceSize, bool vertical, float uvCoordinate)
    {
        var from = vertical ? new Vector2(uvCoordinate, -1f) : new Vector2(-1f, uvCoordinate);
        var to = vertical ? new Vector2(uvCoordinate, 2f) : new Vector2(2f, uvCoordinate);
        var a = _projection.CanvasToScreen(sourceOrigin + from * sourceSize);
        var b = _projection.CanvasToScreen(sourceOrigin + to * sourceSize);
        dl.AddLine(a, b, UiColors.StatusAnimated.Fade(0.6f), 1 * T3Ui.UiScaleFactor);
    }

    // Slice editing: the label drag, its start, the slice's quad (reused per slice), and its menu.
    private bool _sliceLabelDragging;
    private (Vector2 Origin, Vector4 Uv)? _sliceMoveStart;
    private readonly Vector2[] _sliceQuadBuffer = new Vector2[4];
    private const string SliceMenuId = "##sliceMenu";

    // Consumer labels ("→ P1, Wall"), rebuilt per structure change.
    private readonly System.Text.StringBuilder _consumerText = new();
    private readonly Dictionary<Guid, string> _consumerLabels = [];
    private int _consumerLabelsVersion = -1;
}
