#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Region editing wherever the parent surface is seen flat — its card on the Board, its rectified photo:
/// the region lives in its parent's metre space (Y up, origin at the parent's anchor), and a
/// <see cref="RegionProjection"/> carries that space to the screen. Corners resize about the opposite corner,
/// edges crop, the label moves the whole rectangle; everything snaps to the parent's and the siblings' edges
/// and centres, and every gesture is one <see cref="RunResizeDrag"/> step. The projector view keeps its own
/// region editor, whose handles ride the corner pin.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>Parent-space metres → screen: the parent's origin in the view's own canvas space, an optional
    /// homography into that view (the photo's rectification), then the view's projection.</summary>
    private sealed class RegionProjection : ICanvasProjection
    {
        public ICanvasProjection View = null!;
        public Vector2 Origin;
        public bool UseHomography;
        public Homography ToView;
        public Homography FromView;

        public Vector2 CanvasToScreen(Vector2 posInCanvas)
        {
            var p = Origin + posInCanvas;
            if (UseHomography)
                p = ToView.TransformPoint(p);

            return View.CanvasToScreen(p);
        }

        public Vector2 ScreenToCanvas(Vector2 posOnScreen)
        {
            var p = View.ScreenToCanvas(posOnScreen);
            if (UseHomography)
                p = FromView.TransformPoint(p);

            return p - Origin;
        }
    }

    /// <summary>
    /// A region in its parent's space: outline and label always; corner, edge and move handles while it is
    /// selected and the view is settled (<paramref name="fade"/> 1).
    /// </summary>
    private void DrawRegionEditable(Setup setup, ImDrawListPtr dl, Surface parent, Surface child, RegionProjection projection,
                                    SetupEntitySelection? selection, float fade)
    {
        var scale = T3Ui.UiScaleFactor;
        SurfaceGeometry.ChildBounds(child, out var min, out var max);
        var corners = SurfaceGeometry.RectFromBounds(min, max);
        Span<Vector2> screen = stackalloc Vector2[4];
        for (var c = 0; c < 4; c++)
            screen[c] = projection.CanvasToScreen(corners[c]);

        var isSelected = selection?.IsSelected(SetupEntitySelection.EntityKind.Surface, child.Id) ?? false;
        var pulse = isSelected ? 0f : FrameStats.GetPulse(child.Id);
        var color = PulseColor(SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface).Fade(isSelected ? 1f : 0.6f), pulse).Fade(fade);
        var editable = isSelected && fade >= 0.999f;

        // The region's own slice, at the preview opacity — over whatever its parent shows underneath.
        var preview = UserSettings.Config.OutputSetupContentPreview;
        if (preview > 0.01f && OutputManager.TryGetSurfaceSlice(child.Id, out _, out var content, out var uv) && content is { IsDisposed: false })
        {
            var contentSrv = SrvManager.GetSrvForTexture(content);
            if (contentSrv is { IsDisposed: false })
                dl.AddImageQuad(contentSrv.NativePointer, screen[0], screen[1], screen[2], screen[3],
                                new Vector2(uv.X, uv.Y), new Vector2(uv.Z, uv.Y), new Vector2(uv.Z, uv.W), new Vector2(uv.X, uv.W),
                                UiColors.ForegroundFull.Fade(preview * fade));
        }
        if (!editable)
        {
            dl.AddQuad(screen[0], screen[1], screen[2], screen[3], color, 1 * scale);
            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screen, child.Id, child.Name, isSelected, 0.9f * fade, pulse);
            return;
        }

        // The label is the move grip and sits over the middle, where a handle can land under it — the handles
        // yield while the pointer rests on the label, unless a drag is already live.
        var moveActive = _labelMoveSurfaceId == child.Id;
        var pointerOverLabel = !moveActive && !string.IsNullOrEmpty(child.Name) && IsMouseOverLabel(screen, child.Name);
        var style = CornerPinHandles.Style.ForSurface(null, editable: !pointerOverLabel && !moveActive, selected: true);
        style.DrawChecker = false;
        style.EdgeColor = color;

        ImGui.PushID(child.Id.GetHashCode());
        Array.Copy(corners, _regionQuad, 4);
        var cornerPhase = CornerPinHandles.Draw(_regionQuad, projection, style, out var draggedCorner);
        var edgePhase = CanvasPointHandle.DragPhase.None;
        var edge = -1;
        var edgePos = Vector2.Zero;
        if (cornerPhase == CanvasPointHandle.DragPhase.None)
            edgePhase = CornerPinHandles.DrawEdgeHandles(_regionQuad, projection, style, out edge, out edgePos);

        ImGui.PopID();

        var thresholds = RegionSnapThresholds(projection, parent);
        var snapping = !ImGui.GetIO().KeyShift;

        if (draggedCorner >= 0 && cornerPhase != CanvasPointHandle.DragPhase.None)
        {
            var dragged = _regionQuad[draggedCorner];
            HandleChildEdit(cornerPhase, parent, child, () =>
                                                        {
                                                            // Re-based on the pre-drag rectangle each frame; the opposite corner stays.
                                                            _resizeOldState!.Value.Restore(child);
                                                            SurfaceGeometry.ChildBounds(child, out var oldMin, out var oldMax);
                                                            var fixedCorner = draggedCorner switch
                                                                                  {
                                                                                      0 => new Vector2(oldMax.X, oldMin.Y),
                                                                                      1 => oldMin,
                                                                                      2 => new Vector2(oldMin.X, oldMax.Y),
                                                                                      _ => oldMax,
                                                                                  };
                                                            var point = dragged;
                                                            if (snapping)
                                                            {
                                                                SurfaceGeometry.CollectSnapCandidates(setup, parent, child.Id, _snapXs, _snapYs);
                                                                Span<float> x = [point.X];
                                                                if (SurfaceGeometry.TrySnapOffset(_snapXs, x, thresholds.X, out var offsetX, out _))
                                                                    point.X += offsetX;

                                                                Span<float> y = [point.Y];
                                                                if (SurfaceGeometry.TrySnapOffset(_snapYs, y, thresholds.Y, out var offsetY, out _))
                                                                    point.Y += offsetY;
                                                            }

                                                            var newMin = Vector2.Min(point, fixedCorner);
                                                            var newMax = Vector2.Max(point, fixedCorner);
                                                            SurfaceGeometry.SetChildBounds(child, newMin, newMax);
                                                        });
        }
        else if (edge >= 0 && edgePhase != CanvasPointHandle.DragPhase.None)
        {
            HandleChildEdit(edgePhase, parent, child, () =>
                                                      {
                                                          _resizeOldState!.Value.Restore(child);
                                                          SurfaceGeometry.ChildBounds(child, out var newMin, out var newMax);
                                                          var pos = edgePos;
                                                          var horizontal = edge is 1 or 3;
                                                          if (snapping)
                                                          {
                                                              SurfaceGeometry.CollectSnapCandidates(setup, parent, child.Id, _snapXs, _snapYs);
                                                              Span<float> anchor = [horizontal ? pos.X : pos.Y];
                                                              if (SurfaceGeometry.TrySnapOffset(horizontal ? _snapXs : _snapYs, anchor,
                                                                                                horizontal ? thresholds.X : thresholds.Y, out var offset, out _))
                                                              {
                                                                  if (horizontal)
                                                                      pos.X += offset;
                                                                  else
                                                                      pos.Y += offset;
                                                              }
                                                          }

                                                          switch (edge) // 0 = top … 3 = left; parent space is Y up
                                                          {
                                                              case 0: newMax.Y = MathF.Max(pos.Y, newMin.Y + SurfaceGeometry.MinSize); break;
                                                              case 1: newMax.X = MathF.Max(pos.X, newMin.X + SurfaceGeometry.MinSize); break;
                                                              case 2: newMin.Y = MathF.Min(pos.Y, newMax.Y - SurfaceGeometry.MinSize); break;
                                                              default: newMin.X = MathF.Min(pos.X, newMax.X - SurfaceGeometry.MinSize); break;
                                                          }

                                                          SurfaceGeometry.SetChildBounds(child, newMin, newMax);
                                                      });
        }

        HandleRegionLabelMove(setup, parent, child, projection, screen, thresholds, snapping);

        // Re-read: an edit above may have moved the rectangle this frame.
        SurfaceGeometry.ChildBounds(child, out min, out max);
        corners = SurfaceGeometry.RectFromBounds(min, max);
        for (var c = 0; c < 4; c++)
            screen[c] = projection.CanvasToScreen(corners[c]);

        DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screen, child.Id, child.Name, true, fade, pulse);

        // The region's own anchor — the origin of its space, what its own children measure from.
        DrawAnchorGlyph(dl, projection.CanvasToScreen(child.LocalPosition + child.AnchorInMeters), fade);
    }

    /// <summary>The label as the region's move grip: the press selected it (through the picker); the held button moves it.</summary>
    private void HandleRegionLabelMove(Setup setup, Surface parent, Surface child, RegionProjection projection,
                                       ReadOnlySpan<Vector2> screen, Vector2 thresholds, bool snapping)
    {
        if (string.IsNullOrEmpty(child.Name))
            return;

        var phase = CanvasPointHandle.DragPhase.None;
        if (_labelMoveSurfaceId == child.Id)
        {
            phase = ImGui.IsMouseDown(ImGuiMouseButton.Left) ? CanvasPointHandle.DragPhase.Dragging : CanvasPointHandle.DragPhase.Completed;
        }
        else if (_labelMoveSurfaceId == Guid.Empty && _labelGrabScreen != null
                 && ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                 && (ImGui.GetMousePos() - _labelGrabScreen.Value).Length() > UserSettings.Config.ClickThreshold
                 && IsPointOverLabel(screen, child.Name, _labelGrabScreen.Value))
        {
            _labelGrabScreen = null;
            phase = CanvasPointHandle.DragPhase.Started;
        }

        if (phase == CanvasPointHandle.DragPhase.None)
            return;

        RunResizeDrag(phase, child,
                      onDragging: () =>
                                  {
                                      if (_childMoveStart == null)
                                          return;

                                      var (origin, startMin, startMax) = _childMoveStart.Value;
                                      var size = startMax - startMin;
                                      var delta = projection.ScreenToCanvas(ImGui.GetMousePos()) - origin;
                                      var newMin = startMin + delta;
                                      if (snapping)
                                      {
                                          SurfaceGeometry.CollectSnapCandidates(setup, parent, child.Id, _snapXs, _snapYs);
                                          Span<float> xs = [newMin.X, newMin.X + size.X * 0.5f, newMin.X + size.X];
                                          Span<float> ys = [newMin.Y, newMin.Y + size.Y * 0.5f, newMin.Y + size.Y];
                                          if (SurfaceGeometry.TrySnapOffset(_snapXs, xs, thresholds.X, out var offsetX, out _))
                                              newMin.X += offsetX;

                                          if (SurfaceGeometry.TrySnapOffset(_snapYs, ys, thresholds.Y, out var offsetY, out _))
                                              newMin.Y += offsetY;
                                      }

                                      _resizeOldState!.Value.Restore(child);
                                      SurfaceGeometry.SetChildBounds(child, newMin, newMin + size);
                                  },
                      onStarted: () =>
                                 {
                                     SurfaceGeometry.ChildBounds(child, out var startMin, out var startMax);
                                     _labelMoveSurfaceId = child.Id;
                                     _childMoveStart = (projection.ScreenToCanvas(ImGui.GetMousePos()), startMin, startMax);
                                 },
                      onCompleted: () =>
                                   {
                                       _labelMoveSurfaceId = Guid.Empty;
                                       _childMoveStart = null;
                                   });
    }

    /// <summary>A constant screen distance (7 px) in the parent's units, per axis.</summary>
    private static Vector2 RegionSnapThresholds(RegionProjection projection, Surface parent)
    {
        var probe = MathF.Max(MathF.Min(parent.SizeInMeters.X, parent.SizeInMeters.Y) * 0.05f, 0.0001f);
        var origin = projection.CanvasToScreen(Vector2.Zero);
        var pixelsX = Vector2.Distance(origin, projection.CanvasToScreen(new Vector2(probe, 0)));
        var pixelsY = Vector2.Distance(origin, projection.CanvasToScreen(new Vector2(0, probe)));
        var wanted = 7 * T3Ui.UiScaleFactor;
        return new Vector2(pixelsX > 0.001f ? probe / pixelsX * wanted : 0f, pixelsY > 0.001f ? probe / pixelsY * wanted : 0f);
    }

    private readonly Vector2[] _regionQuad = new Vector2[4];
    private readonly RegionProjection _regionProjection = new();
}
