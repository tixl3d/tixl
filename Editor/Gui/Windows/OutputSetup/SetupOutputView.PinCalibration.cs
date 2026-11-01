#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Calibrating a corner pin by its reference points, on the plain projector canvas: each point is a handle
/// that can be walked onto the real feature, a projected disc of the straightened photo shows what to look
/// for, and the pin is re-solved so every set point projects where it was dropped.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// The focused surface's reference points as handles on the projector canvas. An idle point rides the pin
    /// (it shows where the photo's feature currently lands); dragging it sets it — its target is where it was
    /// dropped, in output pixels, and it never moves again on its own. The pin is re-solved from the set targets
    /// alone: exactly as free as they allow (one shifts, two turn and scale, three shear, four keystone; beyond
    /// four the solve averages and the header reports the miss). Double-click or "Release Point" returns a point
    /// to idle. One undo step per drag or release. Everything but the handle and its label is in the composite,
    /// so the canvas shows what the projector gets.
    /// </summary>
    private void DrawReferencePointPins(Setup setup, ImDrawListPtr dl, Surface surface, Surface.OutputMapping mapping, bool editable, float fade)
    {
        var pinCanvas = SurfaceGeometry.CanvasSizeOf(setup, mapping.OutputId);
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, pinCanvas, out var surfaceToOutput))
            return;

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
                if (isAimed)
                    ReleasePoint(setup, mapping, point.Id);

                CancelGesture(); // the press that became this double-click must not also commit a drag
            }
            else if (phase == CanvasPointHandle.DragPhases.Started)
            {
                _activePointId = point.Id;
                BeginGesture(setup, GestureKinds.AimPoint, "Aim reference point", surface.Id);
                SeedPointAims(setup, surface);
                if (mapping.QuadBeforeAligning == null)
                {
                    mapping.QuadBeforeAligning = (Vector2[])mapping.Quad.Clone();
                    mapping.ModeBeforeAligning = mapping.Mode;
                }
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

            var markColor = isAimed ? green.Fade(0.9f * fade) : UiColors.ForegroundFull.Fade(0.5f * fade);
            DrawPointLabel(dl, _projection.CanvasToScreen(px), PointLabel(point.Name, ordinal), markColor);
        }
    }

    /// <summary>
    /// The surface "Align Points" works on: the one the selection resolves to, or, with nothing on this output's
    /// canvas selected, the only surface mapped here that has a photo. With several such surfaces the choice is
    /// the user's, which <paramref name="hasSeveralCandidates"/> reports.
    /// </summary>
    private Surface? FindAlignSubject(Setup setup, Guid outputId, out bool hasSeveralCandidates)
    {
        hasSeveralCandidates = false;
        var selected = SurfaceGeometry.FindMappingCarrier(setup, _shownSurfaceId, outputId);
        if (selected != null)
            return selected.Trace != null ? selected : null;

        Surface? candidate = null;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.Trace == null || !surface.HasMapping(outputId))
                continue;

            if (candidate != null)
            {
                hasSeveralCandidates = true;
                return null;
            }

            candidate = surface;
        }

        return candidate;
    }

    /// <summary>"Release Point": the active point's way back to idle, disabled while it isn't set.</summary>
    private void DrawReleasePointButton(Setup setup, Surface surface, Guid outputId)
    {
        var mapping = surface.FindMapping(outputId);
        var canRelease = mapping != null && mapping.PointAims.TryGetValue(_activePointId, out var aim) && aim.IsAimed;
        ImGui.BeginDisabled(!canRelease);
        if (CustomComponents.StateButton("Release Point", CustomComponents.ButtonStates.Default) && canRelease)
            ReleasePoint(setup, mapping!, _activePointId);

        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(canRelease
                                 ? "The active point stops constraining the pin. It stays where it is, and the pin does not change."
                                 : "Drag a point to set it. The last one you moved is the active one.");
    }

    /// <summary>
    /// "Reset Alignment": forgets every target on this output and puts the pin back to where it was before the
    /// first point was aimed. A pin aligned before that was remembered falls back to filling the canvas, which is
    /// where a new mapping starts.
    /// </summary>
    private void DrawResetAlignmentButton(Setup setup, Surface surface, Guid outputId)
    {
        var mapping = surface.FindMapping(outputId);
        var canReset = mapping != null && (mapping.PointAims.Count > 0 || mapping.QuadBeforeAligning != null || !mapping.IsFilling);
        ImGui.BeginDisabled(!canReset);
        if (CustomComponents.StateButton("Reset Alignment", CustomComponents.ButtonStates.Default) && canReset)
        {
            SetupUndo.RunUndoable("Reset alignment", setup, () => ResetAlignment(mapping!));
            _pinResidualPx = 0;
            _activePointId = Guid.Empty;
        }

        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(canReset
                                 ? mapping!.QuadBeforeAligning != null
                                       ? "Releases every point and puts the corner pin back to where it was before aligning."
                                       : "Releases every point and lets the surface fill the canvas again."
                                 : "The surface fills the canvas and no point has been moved.");
    }

    private static void ResetAlignment(Surface.OutputMapping mapping)
    {
        mapping.PointAims.Clear();
        if (mapping.QuadBeforeAligning == null)
        {
            mapping.FillCanvas();
            return;
        }

        mapping.Quad = mapping.QuadBeforeAligning;
        mapping.Mode = mapping.ModeBeforeAligning;
        mapping.QuadBeforeAligning = null;
    }

    /** Back to idle: the mark stays where it is, it just stops constraining the pin. */
    private static void ReleasePoint(Setup setup, Surface.OutputMapping mapping, Guid pointId)
    {
        SetupUndo.RunUndoable("Release reference point", setup,
                              () => mapping.PointAims[pointId] = mapping.PointAims[pointId] with { IsAimed = false });
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

    /** Whether "Align Points" is on: the reference points are projected and can be dragged. */
    private bool _projectsPhoto;

    /** The point moved last, whose outline blinks on the wall; further moves and "Release Point" apply to it. */
    private Guid _activePointId;

    // Pin solve: the worst miss in pixels, and the scratch lists rebuilt per solve.
    private float _pinResidualPx;
    private readonly List<Vector2> _pinTargets = [];
    private readonly List<Vector2> _pinFrom = [];
    private readonly List<Vector2> _pinSurfacePositions = [];
}
