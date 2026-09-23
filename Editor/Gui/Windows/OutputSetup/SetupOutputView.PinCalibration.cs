#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Calibrating a corner pin by its reference points, on the plain projector canvas: each point is a handle
/// that can be walked onto the real feature, a disc of the straightened photo shows what to look for, and
/// the pin is re-solved so every aimed point projects where it was dropped.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// The focused surface's reference points as handles on the projector canvas. An idle point rides the pin
    /// (it shows where the photo's feature currently lands); dragging it activates it — its target is where it
    /// was dropped, in output pixels, and it never moves again on its own. The pin is re-solved from the
    /// activated targets alone: exactly as free as they allow (one shifts, two turn and scale, three shear,
    /// four keystone; beyond four the solve averages and the header reports the miss). Double-click resets a
    /// point to idle. One undo step per drag or reset.
    /// </summary>
    private void DrawReferencePointPins(Setup setup, ImDrawListPtr dl, Surface surface, Surface.OutputMapping mapping, Guid outputId,
                                        Vector2 canvasSize, bool editable, float fade)
    {
        var pinCanvas = SurfaceGeometry.CanvasSizeOf(setup, mapping.OutputId);
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, pinCanvas, out var surfaceToOutput))
            return;

        // The discs are always on the canvas — they are what says which feature a point marks; the toggle
        // only decides whether they also go to the wall.
        DrawCanvasPhotoDiscs(setup, dl, surface, mapping, surfaceToOutput, canvasSize, fade);

        var green = SetupColors.ForKind(SetupEntityKinds.Surface);
        var idleStyle = CanvasPointHandle.Style.Default(UiColors.ForegroundFull.Fade(0.6f * fade), CanvasPointHandle.Shapes.Circle, editable);
        idleStyle.OutlineColor = UiColors.ForegroundFull.Fade(0.4f * fade);
        idleStyle.Radius = 6;
        var activeStyle = idleStyle;
        activeStyle.Color = UiColors.ForegroundFull.Fade(fade);
        activeStyle.OutlineColor = green.Fade(fade);
        activeStyle.Radius = 7;

        var ordinal = 0;
        for (var i = 0; i < surface.Annotations.Count; i++)
        {
            var point = surface.Annotations[i];
            if (!point.IsPoint)
                continue;

            ordinal++;
            // The mark stands still. Its place is stored as a fraction of the canvas (this view works in the
            // canvas' pixels, so it converts here), seeded once from wherever the pin projected the point when
            // it first appeared on this output. Re-deriving it from the pin every frame would make it agree
            // with the pin by construction — and a mark that cannot disagree with the pin is worth nothing to
            // someone aligning one against a wall.
            var aim = AimOf(mapping, point.Id, point.P1, surfaceToOutput, pinCanvas);
            var px = aim.Position * pinCanvas;
            var isAimed = aim.IsAimed;

            ImGui.PushID(i);
            var phase = CanvasPointHandle.Draw(ref px, _projection, isAimed ? activeStyle : idleStyle);
            var hovered = ImGui.IsItemHovered();
            ImGui.PopID();

            if (editable && hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                // Back to idle: the mark stays where it is, it just stops constraining the pin.
                if (isAimed)
                    SetupUndo.RunUndoable("Reset reference point", setup,
                                             () => mapping.PointAims[point.Id] = aim with { IsAimed = false });

                CancelGesture(); // the press that became this double-click must not also commit a drag
            }
            else if (phase == CanvasPointHandle.DragPhases.Started)
            {
                BeginGesture(setup, GestureKinds.AimPoint, "Aim reference point", surface.Id);
                SeedPointAims(setup, surface);
            }
            else if (phase == CanvasPointHandle.DragPhases.Dragging && _gesture.Is(GestureKinds.AimPoint, surface.Id))
            {
                mapping.PointAims[point.Id] = new Surface.OutputMapping.PointAim(px / pinCanvas, true);
                SolvePinFromTargets(surface, mapping, pinCanvas);
            }
            else if (phase == CanvasPointHandle.DragPhases.Completed)
            {
                EndGesture(setup);
            }

            if (hovered || phase != CanvasPointHandle.DragPhases.None)
                CalibrationOverlay.EmphasizeAnnotation(surface.Id, i);

            // Where the pin currently sends this point. An arrow from the mark to it is the miss, drawn rather
            // than hidden by moving the mark: it says which way and how far the pin is off at this point.
            DrawProjectionArrow(dl, px, surfaceToOutput.TransformPoint(point.P1), fade);

            var screen = _projection.CanvasToScreen(px);
            var markColor = isAimed ? green.Fade(0.9f * fade) : UiColors.ForegroundFull.Fade(0.5f * fade);
            CanvasDraw.Crosshair(dl, screen, markColor, 9f, 1f);
            DrawPointLabel(dl, screen, PointLabel(point.Name, ordinal), markColor);
        }
    }

    /// <summary>
    /// Where a point's mark sits on this output: its stored aim, or — until a gesture has placed it — wherever
    /// the pin projects the point right now.
    /// </summary>
    private static Surface.OutputMapping.PointAim AimOf(Surface.OutputMapping mapping, Guid pointId, Vector2 pointInSurface,
                                                        in Homography surfaceToOutput, Vector2 canvasSize)
    {
        if (mapping.PointAims.TryGetValue(pointId, out var existing))
            return existing;

        // Not yet placed: shown riding the pin, stored only once a gesture starts (see SeedPointAims).
        var projected = surfaceToOutput.TransformPoint(pointInSurface);
        return new Surface.OutputMapping.PointAim(projected / canvasSize, false);
    }

    /// <summary>
    /// Places every mark of the surface that has none yet, at where each pin projects its point right now.
    /// Runs at the start of a gesture that moves a pin or a mark, inside its undo snapshot: from then on the
    /// marks stand still while the pin moves, which is what makes the miss between them visible. Before that
    /// the two agree by construction, so nothing needs storing — and nothing gets written from a mere view.
    /// </summary>
    private static void SeedPointAims(Setup setup, Surface surface)
    {
        foreach (var mapping in surface.OutputMappings)
        {
            var canvasSize = SurfaceGeometry.CanvasSizeOf(setup, mapping.OutputId);
            if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, canvasSize, out var surfaceToOutput))
                continue;

            foreach (var point in surface.Annotations)
            {
                if (!point.IsPoint || mapping.PointAims.ContainsKey(point.Id))
                    continue;

                mapping.PointAims[point.Id] = AimOf(mapping, point.Id, point.P1, surfaceToOutput, canvasSize);
            }
        }
    }

    /// <summary>
    /// The miss at one reference point: from its mark to where the current pin actually projects it. Nothing is
    /// drawn while the two agree — an arrow that is always there stops being a signal.
    /// </summary>
    private void DrawProjectionArrow(ImDrawListPtr dl, Vector2 markInCanvas, Vector2 projectedInCanvas, float fade)
    {
        var from = _projection.CanvasToScreen(markInCanvas);
        var to = _projection.CanvasToScreen(projectedInCanvas);
        var delta = to - from;
        var length = delta.Length();
        var scale = T3Ui.UiScaleFactor;
        if (length < 6 * scale)
            return;

        // Stops short of both ends so the mark and the projected spot stay readable under it.
        var direction = delta / length;
        var start = from + direction * 7 * scale;
        var end = to - direction * 3 * scale;
        var color = UiColors.StatusAttention.Fade(0.8f * fade);
        dl.AddLine(start, end, color, 1.5f * scale);

        var head = 5 * scale;
        var side = new Vector2(-direction.Y, direction.X) * head * 0.5f;
        dl.AddTriangleFilled(end, end - direction * head + side, end - direction * head - side, color);
        CanvasDraw.Crosshair(dl, to, color, 4f, 1f);
    }

    /// <summary>
    /// A disc of the surface's straightened photo around each reference point: the wall's own picture, right
    /// where the feature is, so a mark can be walked onto it. Centred on the marks, not on where the pin
    /// projects them — the disc belongs to its mark and stands still with it.
    /// </summary>
    private void DrawCanvasPhotoDiscs(Setup setup, ImDrawListPtr dl, Surface surface, Surface.OutputMapping mapping,
                                      in Homography surfaceToOutput, Vector2 canvasSize, float fade)
    {
        if (!TryGetTracedFragment(setup, surface, out var fragmentSrv, out var photo, out var uvMin, out var uvMax))
            return;

        // The pin is stored as fractions of the canvas; this warp works in its pixels, like the view around it.
        for (var c = 0; c < 4; c++)
            _canvasDiscQuad[c] = mapping.Quad[c] * canvasSize;

        CanvasDraw.Bounds(_canvasDiscQuad, out var bboxMin, out var bboxMax);
        var bboxSize = bboxMax - bboxMin;

        // A pin that has collapsed — or gone non-finite mid-solve — has no area to warp into: the target would
        // be a pixel or two across and every disc would come out a single smeared colour. That is the state you
        // are most likely to be in while fixing a bad pin, which is exactly when the discs have to be there.
        var pinIsUsable = float.IsFinite(bboxSize.X) && float.IsFinite(bboxSize.Y)
                          && bboxSize.X >= MinDiscWarpExtent && bboxSize.Y >= MinDiscWarpExtent;

        SharpDX.Direct3D11.ShaderResourceView? srv = null;
        if (pinIsUsable)
        {
            var scale = MathF.Min(1f, 2048f / MathF.Max(bboxSize.X, bboxSize.Y));
            for (var c = 0; c < 4; c++)
                _canvasDiscQuad[c] = (_canvasDiscQuad[c] - bboxMin) * scale;

            var size = new T3.Core.DataTypes.Vector.Int2(Math.Max(1, (int)(bboxSize.X * scale)),
                                                         Math.Max(1, (int)(bboxSize.Y * scale)));
            var warped = OutputCompositor.RenderWarpedTexture(photo, _canvasDiscQuad, size, _canvasDiscKey,
                                                           new Vector4(uvMin.X, uvMin.Y, uvMax.X, uvMax.Y));
            srv = warped is { IsDisposed: false } ? SrvManager.GetSrvForTexture(warped) : null;
        }

        // Fall back to the straightened photo itself: upright and a fixed size, so it is wrong about the
        // keystone and right about what the feature looks like — which is all the disc is for.
        var upright = srv is not { IsDisposed: false };
        if (upright)
            srv = fragmentSrv;

        if (srv is not { IsDisposed: false })
            return;

        SurfaceGeometry.LocalBounds(surface, out var surfaceMin, out var surfaceMax);
        var surfaceSpan = Vector2.Max(surfaceMax - surfaceMin, new Vector2(0.0001f));
        var uvSpan = uvMax - uvMin;
        var uvRadius = uvSpan * UserSettings.Config.OutputSetupPhotoDiscRadius;

        var radius = canvasSize.Y * UserSettings.Config.OutputSetupPhotoDiscRadius;
        var tint = UiColors.ForegroundFull.Fade(fade);
        foreach (var point in surface.Annotations)
        {
            if (!point.IsPoint)
                continue;

            var centre = AimOf(mapping, point.Id, point.P1, surfaceToOutput, canvasSize).Position * canvasSize;
            var min = centre - new Vector2(radius);
            var max = centre + new Vector2(radius);

            Vector2 uv0, uv1;
            if (upright)
            {
                // The point's place in the surface's own rectangle, into the fragment's window. Surface metres
                // run Y-up, the photo's V downward.
                var inSurface = new Vector2((point.P1.X - surfaceMin.X) / surfaceSpan.X,
                                            1f - (point.P1.Y - surfaceMin.Y) / surfaceSpan.Y);
                var centreUv = uvMin + uvSpan * inSurface;
                uv0 = centreUv - uvRadius;
                uv1 = centreUv + uvRadius;
            }
            else
            {
                // Sampled around where the pin puts *this* point, not around the mark. The disc has to keep
                // showing the feature it belongs to: reading the warp at the mark would show whatever the photo
                // happens to cover there, so every disc's picture would slide whenever any other point is
                // dragged. It still carries the pin's own distortion, which is what makes it comparable to the
                // wall — only the feature inside it stays the same one.
                var sampled = surfaceToOutput.TransformPoint(point.P1);
                uv0 = (sampled - new Vector2(radius) - bboxMin) / bboxSize;
                uv1 = (sampled + new Vector2(radius) - bboxMin) / bboxSize;
            }

            var screenMin = _projection.CanvasToScreen(min);
            var screenMax = _projection.CanvasToScreen(max);
            dl.AddImageRounded(srv.NativePointer, screenMin, screenMax, uv0, uv1, tint, (screenMax.X - screenMin.X) * 0.5f, ImDrawFlags.RoundCornersAll);
            dl.AddCircle((screenMin + screenMax) * 0.5f, (screenMax.X - screenMin.X) * 0.5f, UiColors.BackgroundFull.Fade(0.5f * fade), 0, 1f);
        }
    }

    /// <summary>
    /// Re-solves the pin so every activated point projects to its target. Up to three targets the solve is
    /// incremental — the transform taking the current projections to the targets, applied to the pin; from
    /// four on it is the (least-squares) homography from surface metres straight to the targets.
    /// </summary>
    private void SolvePinFromTargets(Surface surface, Surface.OutputMapping mapping, Vector2 canvasSize)
    {
        // Solved in the canvas' own 0..1 space, because that is what the quad it writes is stored in — a solve
        // in pixels would put pixel numbers into a normalized pin and throw the surface off the canvas.
        // Vector2.One: the mapping is read and written in the same space, so the two cancel.
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, Vector2.One, out var surfaceToOutput))
            return;

        _pinFrom.Clear();
        _pinTargets.Clear();
        _pinSurfacePositions.Clear();
        foreach (var point in surface.Annotations)
        {
            // Only aimed points constrain: an un-aimed mark is where the pin happened to put the point, so
            // feeding it back in would just ask the solve to keep the pin exactly as it already is.
            if (!point.IsPoint || !mapping.PointAims.TryGetValue(point.Id, out var aim) || !aim.IsAimed)
                continue;

            _pinFrom.Add(surfaceToOutput.TransformPoint(point.P1));
            _pinTargets.Add(aim.Position);
            _pinSurfacePositions.Add(point.P1);
        }

        _pinResidualPx = 0;
        if (_pinTargets.Count == 0)
            return;

        Span<Vector2> quad = stackalloc Vector2[4];
        if (_pinTargets.Count >= 4)
        {
            if (!Homography.TryComputeLeastSquares(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinSurfacePositions),
                                                   System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinTargets), out var surfaceToTargets))
                return;

            Span<Vector2> rect = stackalloc Vector2[4];
            SurfaceGeometry.WriteLocalRect(surface, rect);
            for (var c = 0; c < 4; c++)
            {
                quad[c] = surfaceToTargets.TransformPoint(rect[c]);
                if (!float.IsFinite(quad[c].X) || !float.IsFinite(quad[c].Y))
                    return;
            }

            // The readout is in pixels — that is the unit an operator can judge a miss in.
            for (var i = 0; i < _pinTargets.Count; i++)
            {
                var missed = (surfaceToTargets.TransformPoint(_pinSurfacePositions[i]) - _pinTargets[i]) * canvasSize;
                _pinResidualPx = MathF.Max(_pinResidualPx, missed.Length());
            }
        }
        else
        {
            mapping.Quad.AsSpan(0, 4).CopyTo(quad);
            if (!PointPinSolver.TrySolve(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinFrom),
                                         System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinTargets), quad, out _))
                return;
        }

        mapping.PromoteToCornerPin();
        for (var c = 0; c < 4; c++)
            mapping.Quad[c] = quad[c];
    }

    /// <summary>Canvas pixels a pin's bounding box must span before it is worth warping a photo through.</summary>
    private const float MinDiscWarpExtent = 8f;

    // Projected photo discs: whether the traced photo is projected through the pin, its warp target, and the
    // warp cache key.
    private bool _projectsPhoto;
    private readonly Vector2[] _canvasDiscQuad = new Vector2[4];
    private static readonly Guid _canvasDiscKey = new("6a1f0c2e-7b3d-4e8f-9a0b-1c2d3e4f5a6b");

    // Pin solve: the worst miss in pixels, and the scratch lists rebuilt per solve.
    private float _pinResidualPx;
    private readonly List<Vector2> _pinTargets = [];
    private readonly List<Vector2> _pinFrom = [];
    private readonly List<Vector2> _pinSurfacePositions = [];
}
