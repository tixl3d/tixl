#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The one region editor, wherever a region is seen — its parent's card on the Board, the rectified photo,
/// or the projector canvas through the parent's corner pin. The region lives in its parent's metre space
/// (Y up, origin at the parent's anchor), and a <see cref="RegionProjection"/> carries that space to the
/// screen, so the edits are plain rectangle maths and only the projection differs. Corners resize about the
/// opposite corner, edges crop (the pixels staying put), the body moves the whole rectangle (Alt pans the
/// content under it); everything snaps to the parent's and the siblings' edges and centres, and a
/// nearly-straight move locks to its axis.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>Parent-space metres → screen: the parent's origin in the view's own canvas space, an optional
    /// homography into that view (the photo's rectification, the corner pin), then the view's projection.</summary>
    private sealed class RegionProjection : ICanvasProjection
    {
        public ICanvasProjection View = null!;
        public Vector2 Origin;
        public bool HasHomography;
        public Homography ToView;
        public Homography FromView;

        public Vector2 CanvasToScreen(Vector2 posInCanvas)
        {
            var p = Origin + posInCanvas;
            if (HasHomography)
                p = ToView.TransformPoint(p);

            return View.CanvasToScreen(p);
        }

        public Vector2 ScreenToCanvas(Vector2 posOnScreen)
        {
            var p = View.ScreenToCanvas(posOnScreen);
            if (HasHomography)
                p = FromView.TransformPoint(p);

            return p - Origin;
        }
    }

    /// <summary>
    /// A Layout child on the projector canvas: drawn and edited through its carrier's corner pin and the
    /// view's rectification, in its immediate parent's space. Only the focused (primary) region takes handles
    /// there — a parent recedes while one of its children is the subject.
    /// </summary>
    private void DrawRegionOnOutput(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, in Homography rectifiedToView,
                                    in Homography rectifiedToOutput, Vector2 viewMin, Surface carrier, Surface.OutputMapping carrierMapping,
                                    Surface parent, Surface child, bool editable, float fade)
    {
        if (fade <= 0.01f)
            return;

        var canvasSize = SurfaceGeometry.CanvasSizeOf(setup, carrierMapping.OutputId);
        if (!SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, canvasSize, out var carrierToOutput)
            || !SurfaceGeometry.TryGetOutputToSurface(carrier, carrierMapping, canvasSize, out var outputToCarrier)
            || !SurfaceGeometry.TryGetRegionRect(setup, carrier, child, out _, out _, out var parentOrigin))
            return;

        // Carrier metres → output px → rectified view → framed view: the framing offset folds into the chain
        // so the projection is one homography each way.
        _regionProjection.View = _projection;
        _regionProjection.Origin = parentOrigin;
        _regionProjection.HasHomography = true;
        _regionProjection.ToView = Homography.Multiply(Translation(-viewMin), Homography.Multiply(rectifiedToView, carrierToOutput));
        _regionProjection.FromView = Homography.Multiply(outputToCarrier, Homography.Multiply(rectifiedToOutput, Translation(viewMin)));

        DrawRegionEditable(setup, dl, parent, child, _regionProjection, selection, fade,
                           allowEdit: editable && child.Id == _shownSurfaceId, previewsContent: false);
    }

    /// <summary>
    /// A region in its parent's space: outline and label always; corner, edge and move handles while it is
    /// selected, <paramref name="allowEdit"/> holds and the view is settled (<paramref name="fade"/> 1).
    /// </summary>
    /// <param name="previewsContent">Whether to lay the region's slice over it at the preview opacity — on the
    /// projector canvas the composite already shows it.</param>
    private void DrawRegionEditable(Setup setup, ImDrawListPtr dl, Surface parent, Surface child, RegionProjection projection,
                                    SetupEntitySelection? selection, float fade, bool allowEdit = true, bool previewsContent = true)
    {
        var scale = T3Ui.UiScaleFactor;
        SurfaceGeometry.RegionBounds(child, out var min, out var max);
        SurfaceGeometry.WriteRectCorners(min, max, _regionQuad, yUp: true);
        Span<Vector2> screen = stackalloc Vector2[4];
        for (var c = 0; c < 4; c++)
            screen[c] = projection.CanvasToScreen(_regionQuad[c]);

        // Selection styling covers the whole multi-selection; the shown (primary) region counts as selected too.
        var isSelected = child.Id == _shownSurfaceId || (selection?.IsSelected(SetupEntityKinds.Surface, child.Id) ?? false);
        var pulse = isSelected ? 0f : FrameStats.CrossHighlightAmount(child.Id);
        var hue = SetupColors.ForKind(SetupEntityKinds.Surface);
        var color = PulseColor(hue.Fade(isSelected ? 1f : 0.6f), pulse).Fade(fade);
        var editable = allowEdit && isSelected && fade >= 0.999f;

        // The region's own slice, at the preview opacity — over whatever its parent shows underneath.
        var preview = UserSettings.Config.OutputSetupContentPreviewOpacity;
        if (previewsContent && preview > 0.01f
            && OutputContentResolver.TryGetSurfaceSlice(child.Id, out _, out var content, out var uv) && content is { IsDisposed: false })
        {
            var contentSrv = SrvManager.GetSrvForTexture(content);
            if (contentSrv is { IsDisposed: false })
                dl.AddImageQuad(contentSrv.NativePointer, screen[0], screen[1], screen[2], screen[3],
                                new Vector2(uv.X, uv.Y), new Vector2(uv.Z, uv.Y), new Vector2(uv.Z, uv.W), new Vector2(uv.X, uv.W),
                                UiColors.ForegroundFull.Fade(preview * fade));
        }

        // Hovered from the sidebar: a faint wash behind, the outline carrying the highlight.
        if (pulse > 0.001f)
            dl.AddQuadFilled(screen[0], screen[1], screen[2], screen[3], hue.Fade(pulse * 0.15f * fade));

        // The frame is the pick target, not its name chip. On the Board the parent card hands the pick down
        // (hierarchy rules); anywhere else the region's own rectangle is a background target of its own.
        var onBoard = ReferenceEquals(projection.View, _boardProjection);
        if (!onBoard && fade >= 0.999f)
        {
            CanvasDraw.Bounds(screen, out var bMin, out var bMax);
            _picker.AddTarget(SetupEntityKinds.Surface, child.Id, bMin, bMax, isBackground: true);
        }

        if (!editable)
        {
            dl.AddQuad(screen[0], screen[1], screen[2], screen[3], color, (isSelected ? 2f : 1f) * scale);
            DrawEntityLabel(dl, SetupEntityKinds.Surface, screen, child.Id, child.Name, isSelected, 0.9f * fade, pulse, pickable: false);
            return;
        }

        // The body is the move grip; the handles sit on its outline and take precedence, except while a move is live.
        var moveActive = _gesture.Is(GestureKinds.RegionMove, child.Id) || _gesture.Is(GestureKinds.ContentPan, child.Id);
        var style = CornerPinHandles.Style.ForSurface(null, editable: !moveActive, selected: true, hue: hue);
        style.ShowsChecker = false;
        style.EdgeColor = color;

        ImGui.PushID(child.Id.GetHashCode());
        var cornerPhase = CornerPinHandles.Draw(_regionQuad, projection, style, out var draggedCorner);
        var edgePhase = CanvasPointHandle.DragPhases.None;
        var edge = -1;
        var edgePos = Vector2.Zero;
        if (cornerPhase == CanvasPointHandle.DragPhases.None)
        {
            style.EdgeHandleShape = EdgeDragStretches(child.Id) ? CanvasPointHandle.Shapes.Circle : CanvasPointHandle.Shapes.Square;
            edgePhase = CornerPinHandles.DrawEdgeHandles(_regionQuad, projection, style, out edge, out edgePos);
        }

        ImGui.PopID();

        // Measured at the region, so under perspective the same few pixels catch wherever it sits.
        var probe = MathF.Max(MathF.Min(parent.SizeInMeters.X, parent.SizeInMeters.Y) * 0.05f, 0.0001f);
        var thresholds = RectSnapping.ThresholdFor(projection, (min + max) * 0.5f, probe);
        var snapping = !ImGui.GetIO().KeyShift;

        if (draggedCorner >= 0 && cornerPhase != CanvasPointHandle.DragPhases.None)
            RunRegionCornerDrag(setup, dl, parent, child, projection, cornerPhase, draggedCorner, thresholds, snapping);
        else if (edge >= 0 && edgePhase != CanvasPointHandle.DragPhases.None)
            RunRegionEdgeDrag(setup, dl, parent, child, projection, edgePhase, edge, edgePos, thresholds, snapping);

        HandleRegionLabelMove(setup, dl, parent, child, projection, screen, thresholds, snapping);

        // Re-read: an edit above may have moved the rectangle this frame.
        SurfaceGeometry.RegionBounds(child, out min, out max);
        SurfaceGeometry.WriteRectCorners(min, max, _regionQuad, yUp: true);
        for (var c = 0; c < 4; c++)
            screen[c] = projection.CanvasToScreen(_regionQuad[c]);

        DrawEntityLabel(dl, SetupEntityKinds.Surface, screen, child.Id, child.Name, true, fade, pulse, pickable: false);

        // The region's own anchor — the origin of its space, what its own children measure from.
        DrawAnchorGlyph(dl, projection.CanvasToScreen(child.LocalPosition + child.AnchorInMeters), fade);
    }

    /// <summary>A corner resizes about the opposite corner, re-based on the pre-drag rectangle each frame.</summary>
    private void RunRegionCornerDrag(Setup setup, ImDrawListPtr dl, Surface parent, Surface child, RegionProjection projection,
                                     CanvasPointHandle.DragPhases phase, int draggedCorner, Vector2 thresholds, bool snapping)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                BeginGesture(setup, GestureKinds.SurfaceResize, "Edit region", child.Id, child);
                break;

            case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.SurfaceResize, child.Id):
            {
                var point = _regionQuad[draggedCorner];
                _gesture.Snapshot!.Restore(child);
                SurfaceGeometry.RegionBounds(child, out var oldMin, out var oldMax);
                var fixedCorner = draggedCorner switch
                                      {
                                          0 => new Vector2(oldMax.X, oldMin.Y),
                                          1 => oldMin,
                                          2 => new Vector2(oldMin.X, oldMax.Y),
                                          _ => oldMax,
                                      };

                var guideX = float.NaN;
                var guideY = float.NaN;
                if (snapping)
                {
                    CollectRegionSnapCandidates(setup, parent, child.Id);
                    Span<float> x = [point.X];
                    if (_snapping.TrySnap(RectSnapping.Axes.X, x, thresholds.X, out var offsetX, out guideX))
                        point.X += offsetX;
                    else
                        guideX = float.NaN;

                    Span<float> y = [point.Y];
                    if (_snapping.TrySnap(RectSnapping.Axes.Y, y, thresholds.Y, out var offsetY, out guideY))
                        point.Y += offsetY;
                    else
                        guideY = float.NaN;
                }

                SurfaceGeometry.SetRegionBounds(child, Vector2.Min(point, fixedCorner), Vector2.Max(point, fixedCorner));
                DrawRegionSnapGuides(dl, projection, parent, guideX, guideY);
                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                if (_gesture.Is(GestureKinds.SurfaceResize, child.Id))
                    EndGesture(setup);

                break;
        }
    }

    /// <summary>An edge crops (plain: the pixels stay in place) or stretches (Ctrl: content re-fitted), the opposite edge fixed.</summary>
    private void RunRegionEdgeDrag(Setup setup, ImDrawListPtr dl, Surface parent, Surface child, RegionProjection projection,
                                   CanvasPointHandle.DragPhases phase, int edge, Vector2 edgePos, Vector2 thresholds, bool snapping)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                BeginGesture(setup, GestureKinds.SurfaceResize, "Edit region", child.Id, child);
                _edgeDragStretches = ImGui.GetIO().KeyCtrl;
                if (!_edgeDragStretches)
                    BeginContentEdit(setup, child);

                break;

            case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.SurfaceResize, child.Id):
            {
                // Re-based on the pre-drag rectangle, so the crop's UV derivation never compounds.
                _gesture.Snapshot!.Restore(child);
                SurfaceGeometry.RegionBounds(child, out var newMin, out var newMax);
                var oldMin = newMin;
                var oldMax = newMax;
                var pos = edgePos;
                var horizontal = edge is 1 or 3;
                var guide = float.NaN;
                if (snapping)
                {
                    CollectRegionSnapCandidates(setup, parent, child.Id);
                    Span<float> anchor = [horizontal ? pos.X : pos.Y];
                    if (_snapping.TrySnap(horizontal ? RectSnapping.Axes.X : RectSnapping.Axes.Y, anchor,
                                          horizontal ? thresholds.X : thresholds.Y, out var offset, out guide))
                    {
                        if (horizontal)
                            pos.X += offset;
                        else
                            pos.Y += offset;
                    }
                    else
                    {
                        guide = float.NaN;
                    }
                }

                SurfaceGeometry.MoveEdge(ref newMin, ref newMax, edge, pos, SurfaceGeometry.MinSize);
                SurfaceGeometry.SetRegionBounds(child, newMin, newMax);
                KeepContentInPlace(setup, oldMin, oldMax, newMin, newMax);
                DrawRegionSnapGuides(dl, projection, parent, horizontal ? guide : float.NaN, horizontal ? float.NaN : guide);
                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                if (_gesture.Is(GestureKinds.SurfaceResize, child.Id))
                    EndGesture(setup);

                break;
        }
    }

    /// <summary>
    /// The region's body as its move grip: the press selected it (through the picker); the held button moves
    /// it. Alt at the press pans the content under the region instead. Free movement, but a nearly-straight
    /// drag snaps flat and draws the axis it locked to — placing a region level with its neighbours is the
    /// common case.
    /// </summary>
    private void HandleRegionLabelMove(Setup setup, ImDrawListPtr dl, Surface parent, Surface child, RegionProjection projection,
                                       ReadOnlySpan<Vector2> screen, Vector2 thresholds, bool snapping)
    {
        if (string.IsNullOrEmpty(child.Name))
            return;

        var phase = CanvasPointHandle.DragPhases.None;
        var panning = _gesture.Is(GestureKinds.ContentPan, child.Id)
                      || (!_gesture.IsLive && ImGui.GetIO().KeyAlt && child.SliceId != Guid.Empty);
        var kind = panning ? GestureKinds.ContentPan : GestureKinds.RegionMove;
        if (_gesture.Is(kind, child.Id))
        {
            phase = ImGui.IsMouseDown(ImGuiMouseButton.Left) ? CanvasPointHandle.DragPhases.Dragging : CanvasPointHandle.DragPhases.Completed;
        }
        else if (_picker.IsPicked(child.Id))
        {
            // Anywhere on its body — but only where the region itself is what the press picked.
            CanvasDraw.Bounds(screen, out var bodyMin, out var bodyMax);
            if (TryTakeLabelGrab(bodyMin, bodyMax))
                phase = CanvasPointHandle.DragPhases.Started;
        }

        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                BeginGesture(setup, kind, panning ? "Pan content" : "Move region", child.Id, child,
                             grabPoint: projection.ScreenToCanvas(ImGui.GetMousePos()));
                _childMoveAxis = RectSnapping.AxisLocks.None;
                if (panning)
                    BeginContentEdit(setup, child);

                break;

            case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(kind, child.Id) && _gesture.Snapshot is { } start:
            {
                var startMin = start.LocalPosition;
                var size = start.Size;
                var delta = projection.ScreenToCanvas(ImGui.GetMousePos()) - _gesture.GrabPoint;
                if (panning)
                {
                    if (_gesture.EditsContent)
                        SliceUvAnchoring.ApplyPan(setup, _gesture.ContentSliceId, _gesture.ContentUvStart, delta, size);

                    break;
                }

                _childMoveAxis = snapping ? RectSnapping.LockAxis(ref delta, thresholds) : RectSnapping.AxisLocks.None;

                var newMin = startMin + delta;
                var guideX = float.NaN;
                var guideY = float.NaN;
                if (snapping)
                {
                    // Either edge, or the centre, may catch — whichever is closest wins per axis. The parent's own
                    // edges are candidates, so dropping a region into a corner just lands there.
                    CollectRegionSnapCandidates(setup, parent, child.Id);
                    Span<float> xs = [newMin.X, newMin.X + size.X * 0.5f, newMin.X + size.X];
                    if (_snapping.TrySnap(RectSnapping.Axes.X, xs, thresholds.X, out var offsetX, out guideX))
                        newMin.X += offsetX;
                    else
                        guideX = float.NaN;

                    Span<float> ys = [newMin.Y, newMin.Y + size.Y * 0.5f, newMin.Y + size.Y];
                    if (_snapping.TrySnap(RectSnapping.Axes.Y, ys, thresholds.Y, out var offsetY, out guideY))
                        newMin.Y += offsetY;
                    else
                        guideY = float.NaN;
                }

                _gesture.Snapshot!.Restore(child);
                SurfaceGeometry.SetRegionBounds(child, newMin, newMin + size);
                DrawRegionSnapGuides(dl, projection, parent, guideX, guideY);

                // The locked movement axis, drawn across the parent so it reads as a guide rather than a stub.
                var mid = newMin + size * 0.5f;
                if (_childMoveAxis == RectSnapping.AxisLocks.X)
                    DrawRegionSnapGuides(dl, projection, parent, float.NaN, mid.Y);
                else if (_childMoveAxis == RectSnapping.AxisLocks.Y)
                    DrawRegionSnapGuides(dl, projection, parent, mid.X, float.NaN);

                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                if (_gesture.Is(kind, child.Id))
                    EndGesture(setup);

                _childMoveAxis = RectSnapping.AxisLocks.None;
                break;
        }
    }

    /// <summary>
    /// Coordinates worth snapping to, in the parent's space: the parent's own edges and centre, plus every
    /// sibling's. Snapping in the parent's space (rather than on screen) means alignments survive the
    /// perspective — edges that read as flush stay flush on the wall.
    /// </summary>
    private void CollectRegionSnapCandidates(Setup setup, Surface parent, Guid excludeId)
    {
        _snapping.Clear();
        SurfaceGeometry.LocalBounds(parent, out var parentMin, out var parentMax);
        _snapping.AddRectEdgesAndCentre(parentMin, parentMax);

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var sibling = setup.Surfaces[i];
            if (sibling.ParentId != parent.Id || sibling.Id == excludeId)
                continue;

            SurfaceGeometry.RegionBounds(sibling, out var min, out var max);
            _snapping.AddRectEdgesAndCentre(min, max);
        }
    }

    /// <summary>The caught lines (NaN = none on that axis), in the parent's space, overshooting the parent by its own size on both ends.</summary>
    private static void DrawRegionSnapGuides(ImDrawListPtr dl, RegionProjection projection, Surface parent, float x, float y)
    {
        var size = parent.SizeInMeters;
        SurfaceGeometry.LocalBounds(parent, out var parentMin, out var parentMax);
        var color = UiColors.StatusAnimated.Fade(0.6f);
        var width = 1 * T3Ui.UiScaleFactor;

        if (!float.IsNaN(x))
        {
            var a = projection.CanvasToScreen(new Vector2(x, parentMin.Y - size.Y));
            var b = projection.CanvasToScreen(new Vector2(x, parentMax.Y + size.Y));
            dl.AddLine(a, b, color, width);
        }

        if (!float.IsNaN(y))
        {
            var a = projection.CanvasToScreen(new Vector2(parentMin.X - size.X, y));
            var b = projection.CanvasToScreen(new Vector2(parentMax.X + size.X, y));
            dl.AddLine(a, b, color, width);
        }
    }

    private static Homography Translation(Vector2 offset)
    {
        return new Homography { M11 = 1, M22 = 1, M33 = 1, M13 = offset.X, M23 = offset.Y };
    }

    // The region being drawn, in its parent's space (reused per region), and the projection that carries it to screen.
    private readonly Vector2[] _regionQuad = new Vector2[4];
    private readonly RegionProjection _regionProjection = new();

    // The axis a nearly-straight region move locked to, so the guide can be drawn.
    private RectSnapping.AxisLocks _childMoveAxis;
}
