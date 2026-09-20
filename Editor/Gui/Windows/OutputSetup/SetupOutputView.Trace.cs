#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The surfaces traced on a reference image: their corner-pin quads in photo pixels (on the image's card and
/// in its space), the refine handles on the rectified rect, and the reference points placed on the
/// straightened photo — the spots the projector is later aimed at.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// The surfaces traced on an image as corner-pin quads in photo pixels, through the current projection
    /// (the image's space, or its card on the Board). Editable quads get live handles; a drag is one undo
    /// step through the setup snapshot, like every Board gesture, and selects its surface.
    /// </summary>
    private void DrawTracedQuads(Setup setup, ReferenceImage image, SetupEntitySelection? selection, ImDrawListPtr dl, bool editable, float fade)
    {
        var imageSelected = selection?.IsSelected(SetupEntityKinds.ReferenceImage, image.Id) ?? false;
        Span<Vector2> screenQuad = stackalloc Vector2[4];
        Span<Vector2> localRect = stackalloc Vector2[4];
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var binding = surface.Trace;
            if (binding == null || binding.ImageId != image.Id || binding.Quad.Length < 4)
                continue;

            var isSelected = selection?.IsSelected(SetupEntityKinds.Surface, surface.Id) ?? false;
            var pulse = isSelected ? 0f : FrameStats.CrossHighlightAmount(surface.Id);

            // What the surface shows, laid into its trace — the wall with its content, as it will be. At the
            // preview opacity, so the photo stays the reference; per-triangle, which is close enough for a preview.
            var preview = UserSettings.Config.OutputSetupContentPreviewOpacity;
            if (preview > 0.01f && OutputContentResolver.TryGetSurfaceSlice(surface.Id, out _, out var content, out var uv) && content is { IsDisposed: false })
            {
                var contentSrv = SrvManager.GetSrvForTexture(content);
                if (contentSrv is { IsDisposed: false })
                {
                    for (var c = 0; c < 4; c++)
                        screenQuad[c] = _projection.CanvasToScreen(binding.Quad[c]);

                    dl.AddImageQuad(contentSrv.NativePointer, screenQuad[0], screenQuad[1], screenQuad[2], screenQuad[3],
                                    new Vector2(uv.X, uv.Y), new Vector2(uv.Z, uv.Y), new Vector2(uv.Z, uv.W), new Vector2(uv.X, uv.W),
                                    UiColors.ForegroundFull.Fade(preview * fade));
                }
            }

            ImGui.PushID(surface.Id.GetHashCode());
            var style = CornerPinHandles.Style.ForSurface(null, editable && isSelected, isSelected, fade, hue: SetupColors.ForKind(SetupEntityKinds.Surface));
            style.ShowsChecker = false;
            style.EdgeColor = PulseColor(SetupColors.ForKind(SetupEntityKinds.Surface).Fade(isSelected ? 1f : 0.7f), pulse).Fade(fade);

            var phase = CornerPinHandles.Draw(binding.Quad, _projection, style, out _);
            if (phase == CanvasPointHandle.DragPhases.Started)
            {
                BeginGesture(setup, GestureKinds.TraceCorner, "Trace surface", surface.Id);
                selection?.Select(SetupEntityKinds.Surface, surface.Id);
            }
            else if (phase == CanvasPointHandle.DragPhases.Completed)
            {
                EndGesture(setup);
            }

            ImGui.PopID();

            for (var c = 0; c < 4; c++)
                screenQuad[c] = _projection.CanvasToScreen(binding.Quad[c]);

            DrawEntityLabel(dl, SetupEntityKinds.Surface, screenQuad, surface.Id, surface.Name, isSelected, fade, pulse);

            // Its reference points, where they sit in the photo.
            if (SurfaceMetrics.CountPoints(surface) > 0)
            {
                SurfaceGeometry.WriteLocalRect(surface, localRect);
                if (Homography.TryComputeQuadToQuad(localRect, binding.Quad, out var surfaceToPhoto))
                    DrawReferencePointMarks(dl, surface, surfaceToPhoto, _projection, fade);
            }
        }
    }

    /// <summary>
    /// The traced quads on an image card on the Board, through a projection pointed at the card. On the settled
    /// Board they are as editable as in the image's space; while a space fades the layer they are only drawn.
    /// </summary>
    private void DrawBoardTraces(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, ReferenceImage image, Vector2 min, Vector2 max)
    {
        var fade = _boardLayerFade;
        var pixelSize = BoardPixelSize(setup, SetupEntityKinds.ReferenceImage, image.Id);
        _projection.Origin = new Vector2(min.X, max.Y);
        _projection.PixelsPerMeter = pixelSize.X / MathF.Max(max.X - min.X, 0.0001f);
        DrawTracedQuads(setup, image, selection, dl, fade >= 0.999f, fade);
    }

    /// <summary>
    /// Handles on the rectified rect, in the same grammar as the output canvas: a corner refines the trace —
    /// the rect stays fixed and upright while the traced quad moves under it, so you pull the wall's corner
    /// into the frame and the photo re-warps live — and an edge crops the wall, which takes its declared size
    /// along that edge with it, so the picture keeps its proportions instead of squeezing into a frame that
    /// kept its aspect. Ctrl on a horizontal edge stretches instead: the trace stays and the wall is
    /// re-declared taller or shorter. One undo step per drag. The surface's marks are drawn and edited here
    /// too, mapped from surface metres onto the rect.
    /// </summary>
    private void DrawStraightEdits(Setup setup, ImDrawListPtr dl, Surface subject, Vector2 targetMin, Vector2 targetMax, SetupEntitySelection? selection)
    {
        Span<Vector2> rect = stackalloc Vector2[4];
        SurfaceGeometry.WriteRectCorners(targetMin, targetMax, rect, yUp: false);
        rect.CopyTo(_referenceRectQuad);
        var refining = _gesture.Is(GestureKinds.TraceRefine, subject.Id);
        if (!refining && !Homography.TryComputeQuadToQuad(rect, subject.Trace!.Quad, out _referenceEditToPhoto))
            return;

        ImGui.PushID("straightEdit");
        var style = CornerPinHandles.Style.ForSurface(null, editable: true, selected: true, hue: SetupColors.ForKind(SetupEntityKinds.Surface));
        style.ShowsChecker = false;
        style.EdgeColor = SetupColors.ForKind(SetupEntityKinds.Surface);

        // Only the height can show a stretch — the straightened photo is always drawn at the width it was
        // traced at — so the left and right handles keep saying "crop" whether Ctrl is held or not.
        var stretching = StraightEdgeStretches(subject.Id);
        style.EdgeHandleShape = stretching ? CanvasPointHandle.Shapes.Circle : CanvasPointHandle.Shapes.Square;
        style.VerticalEdgeShape = CanvasPointHandle.Shapes.Square;

        var cornerPhase = CornerPinHandles.Draw(_referenceRectQuad, _projection, style, out var draggedCorner);
        var edgePhase = CanvasPointHandle.DragPhases.None;
        var edge = -1;
        var edgePos = Vector2.Zero;
        if (cornerPhase == CanvasPointHandle.DragPhases.None)
            edgePhase = CornerPinHandles.DrawEdgeHandles(_referenceRectQuad, _projection, style, out edge, out edgePos);

        ImGui.PopID();

        if (edge >= 0 && edgePhase != CanvasPointHandle.DragPhases.None)
        {
            if (stretching && edge is 0 or 2)
                HandleStraightStretch(setup, subject, edgePhase, edgePos);
            else
                HandleStraightCrop(setup, subject, edgePhase, edge, edgePos);
        }
        else
        {
            HandleTraceRefine(setup, subject, cornerPhase, draggedCorner);
            refining = _gesture.Is(GestureKinds.TraceRefine, subject.Id);
        }

        // A live refine moves the photo under the whole frame; its marks are re-expressed as it goes and would
        // only fight the cursor for hover. Every other gesture wants to watch them.
        if (!refining)
            DrawStraightMarks(setup, dl, subject, rect, selection);
    }

    /// <summary>
    /// A corner of the rectified rect: the wall's corner lies elsewhere in the photo than it was traced. The
    /// handle's position through the press-time rectification is where that corner goes, and the surface's
    /// space moves with the trace, so the marks and the pins aiming at the same wall are carried along.
    /// </summary>
    private void HandleTraceRefine(Setup setup, Surface subject, CanvasPointHandle.DragPhases phase, int draggedCorner)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                BeginGesture(setup, GestureKinds.TraceRefine, "Refine trace", subject.Id, subject);
                break;

            case CanvasPointHandle.DragPhases.Completed:
                if (_gesture.Is(GestureKinds.TraceRefine, subject.Id))
                    EndGesture(setup);

                return;
        }

        // Only a live phase carries a handle position; on the release frame the handles already sit back on the
        // rect's corners, so applying then would undo the whole drag.
        if (draggedCorner < 0 || phase == CanvasPointHandle.DragPhases.None
            || !_gesture.Is(GestureKinds.TraceRefine, subject.Id) || _gesture.Snapshot is not { } snapshot
            || subject.Trace is not { Quad.Length: >= 4 } binding)
        {
            return;
        }

        // Re-based from the press: the marks follow the trace, so editing them frame over frame would compound
        // the correction.
        snapshot.Restore(subject);
        binding.Quad[draggedCorner] = _referenceEditToPhoto.TransformPoint(_referenceRectQuad[draggedCorner]);
        CarryTraceRefine(subject, snapshot);
    }

    /// <summary>
    /// An edge of the rectified rect: the wall ends there. The rectangle is cropped to the cursor and its
    /// declared size along that edge goes with it, so the traced photo crops along and the picture keeps its
    /// proportions. The content window follows, as it does for an edge crop on the Board or the output canvas.
    /// </summary>
    private void HandleStraightCrop(Setup setup, Surface subject, CanvasPointHandle.DragPhases phase, int edge, Vector2 edgePos)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                _edgeDragStretches = false;
                BeginGesture(setup, GestureKinds.SurfaceResize, "Crop surface", subject.Id, subject);
                BeginContentEdit(setup, subject);
                break;

            case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.SurfaceResize, subject.Id):
            {
                // Re-based from the press: a crop rewrites the rectangle the cursor is measured against.
                _gesture.Snapshot!.Restore(subject);
                if (!TryGetPressedFrameToSurface(subject, out var frameToSurface))
                    break;

                SurfaceGeometry.LocalBounds(subject, out var oldMin, out var oldMax);
                SurfaceGeometry.DragEdge(subject, edge, frameToSurface.TransformPoint(edgePos), keepDimensions: false);
                SurfaceGeometry.LocalBounds(subject, out var newMin, out var newMax);
                KeepContentInPlace(setup, oldMin, oldMax, newMin, newMax);
                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                if (_gesture.Is(GestureKinds.SurfaceResize, subject.Id))
                    EndGesture(setup);

                break;
        }
    }

    /// <summary>
    /// Ctrl + a horizontal edge: the wall is re-declared taller or shorter while the trace and the photo stay
    /// put. The frame is centred and drawn at the traced width, so the dragged edge's distance from the centre
    /// is the declared half-height — read from the cursor against the rectangle as it was at the press, so a
    /// long drag cannot compound. Lines, points and regions are re-metered along with it.
    /// </summary>
    private void HandleStraightStretch(Setup setup, Surface subject, CanvasPointHandle.DragPhases phase, Vector2 edgePos)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                _edgeDragStretches = true;
                BeginGesture(setup, GestureKinds.SurfaceResize, "Stretch surface", subject.Id, subject);
                break;

            case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.SurfaceResize, subject.Id):
            {
                _gesture.Snapshot!.Restore(subject);
                StraightTargetBounds(subject, out var pressMin, out var pressMax);
                var width = MathF.Max(pressMax.X - pressMin.X, 1f);
                var height = MathF.Max(2 * MathF.Abs(edgePos.Y - (pressMin.Y + pressMax.Y) * 0.5f), 1f);
                SurfaceMetrics.RemeterSurface(setup, subject, new Vector2(subject.SizeInMeters.X,
                                                                          subject.SizeInMeters.X * height / width));
                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                if (_gesture.Is(GestureKinds.SurfaceResize, subject.Id))
                    EndGesture(setup);

                break;
        }
    }

    /// <summary>The rectified frame as it stood at the press, back into the surface's metres — what turns a
    /// cursor on the photo into one absolute edge position, however long the drag runs.</summary>
    private static bool TryGetPressedFrameToSurface(Surface subject, out Homography frameToSurface)
    {
        StraightTargetBounds(subject, out var pressMin, out var pressMax);
        Span<Vector2> frame = stackalloc Vector2[4];
        SurfaceGeometry.WriteRectCorners(pressMin, pressMax, frame, yUp: false);
        Span<Vector2> local = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(subject, local);
        return Homography.TryComputeQuadToQuad(frame, local, out frameToSurface);
    }

    /// <summary>Whether an edge drag on the straightened photo stretches rather than crops: Ctrl at the press,
    /// then the mode the live gesture started in, so releasing Ctrl mid-drag changes nothing.</summary>
    private bool StraightEdgeStretches(Guid surfaceId)
    {
        return _gesture.Is(GestureKinds.SurfaceResize, surfaceId) ? _edgeDragStretches : ImGui.GetIO().KeyCtrl;
    }

    /// <summary>Measuring lines, regions and reference points: surface metres ↔ the rectified rect, a plain
    /// scale (Y up in metres, down in px).</summary>
    private void DrawStraightMarks(Setup setup, ImDrawListPtr dl, Surface subject, ReadOnlySpan<Vector2> rect, SetupEntitySelection? selection)
    {
        Span<Vector2> localRect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(subject, localRect);
        if (!Homography.TryComputeQuadToQuad(localRect, rect, out var surfaceToRect)
            || !Homography.TryComputeQuadToQuad(rect, localRect, out var rectToSurface))
        {
            return;
        }

        DrawAnnotations(dl, subject, surfaceToRect, rectToSurface, Vector2.Zero, editable: true, fade: 1f, projected: false);
        DrawStraightRegions(setup, dl, subject, subject, Vector2.Zero, surfaceToRect, rectToSurface, selection);
        DrawReferencePoints(setup, dl, subject, surfaceToRect, rectToSurface);
    }


    /// <summary>
    /// The trace just moved under the surface's space — dragging a corner or an edge says the wall lies
    /// elsewhere in the photo. The marks name features of that photo, not places in the frame, so they are
    /// re-expressed into the moved space, and the pins aiming at the same wall follow it.
    /// </summary>
    private static void CarryTraceRefine(Surface subject, SurfaceRectSnapshot snapshot)
    {
        if (snapshot.TraceQuad.Length < 4 || subject.Trace is not { Quad.Length: >= 4 } binding)
            return;

        Span<Vector2> localRect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(subject, localRect);
        if (!Homography.TryComputeQuadToQuad(snapshot.TraceQuad, localRect, out var photoToOldSurface)
            || !Homography.TryComputeQuadToQuad(localRect, binding.Quad, out var newSurfaceToPhoto))
        {
            return;
        }

        SurfaceGeometry.CarrySpaceChange(subject, localRect, Homography.Multiply(photoToOldSurface, newSurfaceToPhoto),
                                         solvedTrace: true);
    }

    /// <summary>
    /// The surface's reference points on the rectified photo: placed by a click while "+ Point" is armed,
    /// dragged by their handle (Shift for precision), removed from their right-click menu. Stored in surface
    /// metres, so they are the same spots the projector will be aimed at.
    /// </summary>
    private void DrawReferencePoints(Setup setup, ImDrawListPtr dl, Surface subject, in Homography surfaceToRect, in Homography rectToSurface)
    {
        var color = SetupColors.ForKind(SetupEntityKinds.Surface);

        if (_isPointToolArmed && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var position = rectToSurface.TransformPoint(_projection.ScreenToCanvas(ImGui.GetMousePos()));
            SetupActions.AddReferencePoint(setup, subject, position);
            _isPointToolArmed = false;
        }

        var toDelete = -1;
        var ordinal = 0;
        for (var i = 0; i < subject.Annotations.Count; i++)
        {
            var point = subject.Annotations[i];
            if (!point.IsPoint)
                continue;

            ordinal++;
            var px = surfaceToRect.TransformPoint(point.P1);
            ImGui.PushID(i);
            var style = CanvasPointHandle.Style.Default(UiColors.ForegroundFull, CanvasPointHandle.Shapes.Circle, true);
            style.OutlineColor = color;
            style.Radius = 6;
            var phase = CanvasPointHandle.Draw(ref px, _projection, style);
            var hovered = ImGui.IsItemHovered();
            ImGui.PopID();

            if (phase == CanvasPointHandle.DragPhases.Started)
                BeginGesture(setup, GestureKinds.ReferencePoint, "Move reference point", subject.Id);

            if (phase is CanvasPointHandle.DragPhases.Started or CanvasPointHandle.DragPhases.Dragging)
                point.P1 = point.P2 = rectToSurface.TransformPoint(px);
            else if (phase == CanvasPointHandle.DragPhases.Completed)
                EndGesture(setup);

            var screen = _projection.CanvasToScreen(px);
            CanvasDraw.Crosshair(dl, screen, color.Fade(0.8f), 9f, 1f);
            DrawPointLabel(dl, screen, PointLabel(point.Name, ordinal), color);

            if (hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
            {
                _pointMenuIndex = i;
                ImGui.OpenPopup(PointMenuId);
            }
        }

        if (ImGui.BeginPopup(PointMenuId))
        {
            if (CustomComponents.DrawMenuItem(1, "Delete"))
                toDelete = _pointMenuIndex;

            ImGui.EndPopup();
        }

        if (toDelete >= 0 && toDelete < subject.Annotations.Count)
            SetupUndo.RunUndoable("Delete reference point", setup, () => subject.Annotations.RemoveAt(toDelete));
    }

    /// <summary>A point's name chip, offset to the upper right so the crosshair stays readable.</summary>
    private static void DrawPointLabel(ImDrawListPtr dl, Vector2 screen, string label, T3.Core.DataTypes.Vector.Color color)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.PushFont(Fonts.FontSmall);
        var size = ImGui.CalcTextSize(label);
        ImGui.PopFont();
        // Straight above the mark, centred — an offset to the side reads as belonging to something else.
        var min = screen + new Vector2(-size.X * 0.5f - 3 * scale, -10 * scale - size.Y);
        var max = min + size + new Vector2(6, 2) * scale;
        dl.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.7f), 3 * scale);
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, min + new Vector2(3, 1) * scale, color, label);
    }

    /// <summary>Read-only marks for a surface's reference points, through any surface-space → screen mapping.</summary>
    private static void DrawReferencePointMarks(ImDrawListPtr dl, Surface surface, in Homography surfaceToView, ICanvasProjection view, float fade)
    {
        var color = SetupColors.ForKind(SetupEntityKinds.Surface).Fade(0.8f * fade);
        var ordinal = 0;
        foreach (var point in surface.Annotations)
        {
            if (!point.IsPoint)
                continue;

            ordinal++;
            var screen = view.CanvasToScreen(surfaceToView.TransformPoint(point.P1));
            CanvasDraw.Crosshair(dl, screen, color, 5f, 1f);
            DrawPointLabel(dl, screen, PointLabel(point.Name, ordinal), color);
        }
    }

    /// <summary>
    /// The regions on the rectified photo, nested recursively: each edits in its parent's space, whose origin is
    /// given in the subject's (carrier) space, through the rectification into the photo's px.
    /// </summary>
    private void DrawStraightRegions(Setup setup, ImDrawListPtr dl, Surface subject, Surface parent, Vector2 parentOriginInSubject,
                                     in Homography surfaceToRect, in Homography rectToSurface, SetupEntitySelection? selection)
    {
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var child = setup.Surfaces[i];
            if (child.ParentId != parent.Id)
                continue;

            _regionProjection.View = _projection;
            _regionProjection.Origin = parentOriginInSubject;
            _regionProjection.HasHomography = true;
            _regionProjection.ToView = surfaceToRect;
            _regionProjection.FromView = rectToSurface;
            DrawRegionEditable(setup, dl, parent, child, _regionProjection, selection, 1f);

            SurfaceGeometry.RegionBounds(child, out var localMin, out _);
            DrawStraightRegions(setup, dl, subject, child, parentOriginInSubject + localMin + child.AnchorInMeters, surfaceToRect, rectToSurface, selection);
        }
    }

    // A live handle drag on the rectified rect: the press-time rect→photo mapping, and the handle positions.
    private Homography _referenceEditToPhoto;
    private readonly Vector2[] _referenceRectQuad = new Vector2[4];

    // Reference points on the rectified photo: the "+ Point" tool and the per-point menu.
    private bool _isPointToolArmed; // "+ Point": the next click on the straightened photo places a reference point
    private const string PointMenuId = "##referencePointMenu";
    private int _pointMenuIndex = -1;
}
