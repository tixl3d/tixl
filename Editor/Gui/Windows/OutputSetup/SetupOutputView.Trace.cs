#nullable enable
using ImGuiNET;
using T3.Core.Output;
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
                Span<Vector2> localRect = stackalloc Vector2[4];
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
    /// Handles on the rectified rect. The rect stays fixed and upright; dragging a corner or an edge moves the
    /// traced quad live so the photo re-warps under it — you pull the wall's corner (or edge) into the frame.
    /// The mapping from handle to photo is the rectification at press time, so the drag can't chase its own
    /// re-warp; on release nothing moves. One undo step per drag. The surface's measuring lines are drawn and
    /// edited here too, mapped from surface metres onto the rect.
    /// </summary>
    private void DrawStraightEdits(Setup setup, ImDrawListPtr dl, Surface subject, Vector2 targetMin, Vector2 targetMax, SetupEntitySelection? selection)
    {
        var binding = subject.Trace!;
        Span<Vector2> rect = stackalloc Vector2[4];
        SurfaceGeometry.WriteRectCorners(targetMin, targetMax, rect, yUp: false);
        rect.CopyTo(_referenceRectQuad);
        var refining = _gesture.Is(GestureKinds.TraceRefine, subject.Id);
        if (!refining && !Homography.TryComputeQuadToQuad(rect, binding.Quad, out _referenceEditToPhoto))
            return;

        ImGui.PushID("straightEdit");
        var style = CornerPinHandles.Style.ForSurface(null, editable: true, selected: true, hue: SetupColors.ForKind(SetupEntityKinds.Surface));
        style.ShowsChecker = false;
        style.EdgeColor = SetupColors.ForKind(SetupEntityKinds.Surface);
        var cornerPhase = CornerPinHandles.Draw(_referenceRectQuad, _projection, style, out var draggedCorner);
        var edgePhase = CanvasPointHandle.DragPhases.None;
        var edge = -1;
        var edgePos = Vector2.Zero;
        if (cornerPhase == CanvasPointHandle.DragPhases.None)
            edgePhase = CornerPinHandles.DrawEdgeHandles(_referenceRectQuad, _projection, style, out edge, out edgePos);

        ImGui.PopID();

        // An edge moves along its normal only: a crop of the trace, axis-aligned on the rectified wall.
        if (edge >= 0 && edgePhase != CanvasPointHandle.DragPhases.None)
        {
            switch (edge)
            {
                case 0: _referenceRectQuad[0].Y = _referenceRectQuad[1].Y = edgePos.Y; break;
                case 1: _referenceRectQuad[1].X = _referenceRectQuad[2].X = edgePos.X; break;
                case 2: _referenceRectQuad[2].Y = _referenceRectQuad[3].Y = edgePos.Y; break;
                default: _referenceRectQuad[3].X = _referenceRectQuad[0].X = edgePos.X; break;
            }
        }

        var phase = cornerPhase != CanvasPointHandle.DragPhases.None ? cornerPhase : edgePhase;
        if (phase == CanvasPointHandle.DragPhases.Started)
        {
            BeginGesture(setup, GestureKinds.TraceRefine, "Refine trace", subject.Id);
            refining = true;
        }

        // Only a live phase carries a handle position; on the release frame the handles already sit back on the
        // rect's corners, so applying then would undo the whole drag.
        if (phase is CanvasPointHandle.DragPhases.Started or CanvasPointHandle.DragPhases.Dragging && refining)
        {
            // The handle's position through the press-time rectification is where that corner lies in the photo.
            if (draggedCorner >= 0)
                binding.Quad[draggedCorner] = _referenceEditToPhoto.TransformPoint(_referenceRectQuad[draggedCorner]);
            else if (edge >= 0)
            {
                var a = edge;
                var b = (edge + 1) % 4;
                binding.Quad[a] = _referenceEditToPhoto.TransformPoint(_referenceRectQuad[a]);
                binding.Quad[b] = _referenceEditToPhoto.TransformPoint(_referenceRectQuad[b]);
            }
        }

        if (phase == CanvasPointHandle.DragPhases.Completed)
        {
            EndGesture(setup);
            refining = false;
        }

        // Measuring lines: surface metres ↔ the rectified rect, a plain scale (Y up in metres, down in px).
        Span<Vector2> localRect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(subject, localRect);
        if (!refining
            && Homography.TryComputeQuadToQuad(localRect, rect, out var surfaceToRect)
            && Homography.TryComputeQuadToQuad(rect, localRect, out var rectToSurface))
        {
            DrawAnnotations(dl, subject, surfaceToRect, rectToSurface, Vector2.Zero, editable: true, fade: 1f, projected: false);
            DrawStraightRegions(setup, dl, subject, subject, Vector2.Zero, surfaceToRect, rectToSurface, selection);
            DrawReferencePoints(setup, dl, subject, surfaceToRect, rectToSurface);
        }
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
