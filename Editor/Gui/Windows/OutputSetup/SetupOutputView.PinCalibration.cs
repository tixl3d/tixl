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
    /// four the outer four define it and the rest refine it while they agree, faded while they don't — see
    /// <see cref="TrySolvePin"/>). Double-click or "Release Point" returns a point
    /// to idle. One undo step per drag or release. Everything but the handle and its label is in the composite,
    /// so the canvas shows what the projector gets.
    /// </summary>
    private void DrawReferencePointPins(Setup setup, ImDrawListPtr dl, AlignSubject subject, Surface.OutputMapping mapping, bool editable, float fade)
    {
        var surface = subject.Pinned;
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
        var ignoredStyle = activeStyle;
        ignoredStyle.Color = UiColors.ForegroundFull.Fade(CalibrationOverlay.IgnoredPointOpacity * fade);
        ignoredStyle.OutlineColor = green.Fade(CalibrationOverlay.IgnoredPointOpacity * fade);

        // Classified every frame, not only while dragging, so the ignored points stay faded at rest too.
        Span<Vector2> unusedQuad = stackalloc Vector2[4];
        TrySolvePin(subject, mapping, pinCanvas, unusedQuad);

        var ordinal = 0;
        var points = subject.Traced.Annotations;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (!point.IsPoint)
                continue;

            ordinal++;
            // The mark stands still. Its place is stored as a fraction of the canvas (this view works in the
            // canvas' pixels, so it converts here), seeded once from wherever the pin projected the point when
            // it first appeared on this output. Re-deriving it from the pin every frame would make it agree
            // with the pin by construction — and a mark that cannot disagree with the pin is worth nothing to
            // someone aligning one against a wall.
            var aim = AimOf(mapping, point.Id, point.P1 + subject.TracedToPinned, surfaceToOutput, pinCanvas);
            var px = aim.Position * pinCanvas;
            var isAimed = aim.IsAimed;
            var isIgnored = _ignoredPointIds.Contains(point.Id);

            ImGui.PushID(i);
            var phase = CanvasPointHandle.Draw(ref px, _projection, isIgnored ? ignoredStyle : isAimed ? activeStyle : idleStyle);
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
                BeginPointMove(setup, subject, mapping);
            }
            else if (phase == CanvasPointHandle.DragPhases.Dragging && _gesture.Is(GestureKinds.AimPoint, surface.Id))
            {
                mapping.PointAims[point.Id] = new Surface.OutputMapping.PointAim(px / pinCanvas, true);
                SolvePinFromTargets(subject, mapping, pinCanvas);
            }
            else if (phase == CanvasPointHandle.DragPhases.Completed)
            {
                EndGesture(setup);
            }

            var markColor = isIgnored ? green.Fade(CalibrationOverlay.IgnoredPointOpacity * fade)
                            : isAimed ? green.Fade(0.9f * fade)
                            : UiColors.ForegroundFull.Fade(0.5f * fade);
            DrawPointLabel(dl, _projection.CanvasToScreen(px), PointLabel(point.Name, ordinal), markColor);
        }

        if (editable)
            UpdatePointsFromGamepad(setup, subject, mapping, pinCanvas);
    }

    /** Opens the undo step of moving a point, and remembers the pin if this is the first move of an alignment. */
    private void BeginPointMove(Setup setup, AlignSubject subject, Surface.OutputMapping mapping)
    {
        BeginGesture(setup, GestureKinds.AimPoint, "Aim reference point", subject.Pinned.Id);
        SeedPointAims(setup, subject);
        if (mapping.QuadBeforeAligning == null)
        {
            mapping.QuadBeforeAligning = (Vector2[])mapping.Quad.Clone();
            mapping.ModeBeforeAligning = mapping.Mode;
        }
    }

    /// <summary>
    /// What "Calibrate" calibrates: the surface whose pin is solved, and the traced surface whose photo and
    /// reference points it is aligned by. Both are the same surface, unless a region has a pin of its own while
    /// the photo belongs to an ancestor; then <see cref="TracedToPinned"/> carries the points into the region's space.
    /// </summary>
    private readonly record struct AlignSubject(Surface Pinned, Surface Traced, Vector2 TracedToPinned);

    /// <summary>
    /// The subject "Calibrate" works on: the one the selection resolves to, or, with nothing on this output's
    /// canvas selected, the only surface mapped here that has a photo. With several such surfaces the choice is
    /// the user's, which <paramref name="hasSeveralCandidates"/> reports.
    /// </summary>
    private bool TryFindAlignSubject(Setup setup, Guid outputId, out AlignSubject subject, out bool hasSeveralCandidates)
    {
        hasSeveralCandidates = false;
        var selected = SurfaceGeometry.FindMappingCarrier(setup, _shownSurfaceId, outputId);
        if (selected != null)
            return TryGetAlignSubject(setup, selected, out subject);

        subject = default;
        var isFound = false;
        foreach (var surface in setup.Surfaces)
        {
            if (!surface.HasMapping(outputId) || !TryGetAlignSubject(setup, surface, out var candidate))
                continue;

            if (isFound)
            {
                hasSeveralCandidates = true;
                subject = default;
                return false;
            }

            subject = candidate;
            isFound = true;
        }

        return isFound;
    }

    private static bool TryGetAlignSubject(Setup setup, Surface pinned, out AlignSubject subject)
    {
        subject = default;
        var traced = FindTraceCarrier(setup, pinned.Id);
        if (traced == null)
            return false;

        var tracedToPinned = Vector2.Zero;
        if (traced != pinned)
        {
            if (!SurfaceGeometry.TryGetRegionRect(setup, traced, pinned, out var regionMin, out _, out _))
                return false;

            // Both spaces are metres on the same plane, measured from each surface's anchor.
            tracedToPinned = -regionMin - pinned.AnchorInMeters;
        }

        subject = new AlignSubject(pinned, traced, tracedToPinned);
        return true;
    }

    /// <summary>
    /// The window of the traced surface's photo that the pinned region covers. The photo is straightened for the
    /// traced surface, so its UV range has to be narrowed to where the region sits on it.
    /// </summary>
    private static void NarrowPhotoUvToPinned(AlignSubject subject, ref Vector2 uvMin, ref Vector2 uvMax)
    {
        if (subject.Traced == subject.Pinned)
            return;

        SurfaceGeometry.LocalBounds(subject.Traced, out var tracedMin, out var tracedMax);
        var tracedSpan = Vector2.Max(tracedMax - tracedMin, new Vector2(0.0001f));
        SurfaceGeometry.LocalBounds(subject.Pinned, out var pinnedMin, out var pinnedMax);
        var regionMin = (pinnedMin - subject.TracedToPinned - tracedMin) / tracedSpan;
        var regionMax = (pinnedMax - subject.TracedToPinned - tracedMin) / tracedSpan;

        // Metres run up, V runs down.
        var uvSpan = uvMax - uvMin;
        var narrowedMin = new Vector2(uvMin.X + uvSpan.X * regionMin.X, uvMin.Y + uvSpan.Y * (1 - regionMax.Y));
        var narrowedMax = new Vector2(uvMin.X + uvSpan.X * regionMax.X, uvMin.Y + uvSpan.Y * (1 - regionMin.Y));
        uvMin = narrowedMin;
        uvMax = narrowedMax;
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
                                 ? "The active point stops constraining the pin and returns to where the pin lands it. The pin does not change."
                                 : "Drag a point to set it. The last one you moved is the active one.");
    }

    /// <summary>
    /// "Reset": forgets every target on this output and puts the pin back to where it was before the
    /// first point was aimed. A pin aligned before that was remembered falls back to filling the canvas, which is
    /// where a new mapping starts.
    /// </summary>
    private void DrawResetAlignmentButton(Setup setup, Surface surface, Guid outputId)
    {
        var mapping = surface.FindMapping(outputId);
        var canReset = mapping != null && (mapping.PointAims.Count > 0 || mapping.QuadBeforeAligning != null || !mapping.IsFilling);
        ImGui.BeginDisabled(!canReset);
        if (CustomComponents.StateButton("Reset", CustomComponents.ButtonStates.Default) && canReset)
        {
            ResetAlignment(setup, mapping!);
        }

        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(canReset
                                 ? mapping!.QuadBeforeAligning != null
                                       ? "Releases every point and puts the corner pin back to where it was before calibrating."
                                       : "Releases every point and lets the surface fill the canvas again."
                                 : "The surface fills the canvas and no point has been moved.");
    }

    private void ResetAlignment(Setup setup, Surface.OutputMapping mapping)
    {
        SetupUndo.RunUndoable("Reset calibration", setup, () => RestorePinBeforeAligning(mapping));
        _pinResidualPx = 0;
        _activePointId = Guid.Empty;
    }

    private static void RestorePinBeforeAligning(Surface.OutputMapping mapping)
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

    /** Back to idle: the point stops constraining the pin and returns to where the pin lands it. */
    private static void ReleasePoint(Setup setup, Surface.OutputMapping mapping, Guid pointId)
    {
        SetupUndo.RunUndoable("Release reference point", setup, () => mapping.PointAims.Remove(pointId));
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
    private static void SeedPointAims(Setup setup, AlignSubject subject)
    {
        foreach (var mapping in subject.Pinned.OutputMappings)
        {
            var canvasSize = SurfaceGeometry.CanvasSizeOf(setup, mapping.OutputId);
            if (!SurfaceGeometry.TryGetSurfaceToOutput(subject.Pinned, mapping, canvasSize, out var surfaceToOutput))
                continue;

            foreach (var point in subject.Traced.Annotations)
            {
                if (!point.IsPoint || mapping.PointAims.ContainsKey(point.Id))
                    continue;

                mapping.PointAims[point.Id] = AimOf(mapping, point.Id, point.P1 + subject.TracedToPinned, surfaceToOutput, canvasSize);
            }
        }
    }

    /// <summary>
    /// Re-solves the pin so every activated point projects to its target. Up to three targets the solve is
    /// incremental — the transform taking the current projections to the targets, applied to the pin. From four
    /// on, see <see cref="TrySolvePin"/>.
    /// </summary>
    private void SolvePinFromTargets(AlignSubject subject, Surface.OutputMapping mapping, Vector2 canvasSize)
    {
        Span<Vector2> quad = stackalloc Vector2[4];
        if (!TrySolvePin(subject, mapping, canvasSize, quad))
            return;

        mapping.PromoteToCornerPin();
        for (var c = 0; c < 4; c++)
            mapping.Quad[c] = quad[c];
    }

    /// <summary>
    /// The pin the aimed points ask for, without applying it. From four aimed points on, the four spread furthest
    /// apart on the surface define it exactly, and every other aimed point refines it only while that pin already
    /// lands it near its target. A point that disagrees is ignored, so it can't tear the pin away from the outer
    /// four, and neither can a solve that would fold the pin. Also fills <see cref="_ignoredPointIds"/> and the
    /// largest miss among the points used.
    /// </summary>
    private bool TrySolvePin(AlignSubject subject, Surface.OutputMapping mapping, Vector2 canvasSize, Span<Vector2> quad)
    {
        _ignoredPointIds.Clear();
        _pinResidualPx = 0;

        // Solved in the canvas' own 0..1 space, because that is what the quad it writes is stored in — a solve
        // in pixels would put pixel numbers into a normalized pin and throw the surface off the canvas.
        // Vector2.One: the mapping is read and written in the same space, so the two cancel.
        var surface = subject.Pinned;
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, Vector2.One, out var surfaceToOutput))
            return false;

        _pinFrom.Clear();
        _pinTargets.Clear();
        _pinSurfacePositions.Clear();
        _pinPointIds.Clear();
        foreach (var point in subject.Traced.Annotations)
        {
            // Only aimed points constrain: an un-aimed mark is where the pin happened to put the point, so
            // feeding it back in would just ask the solve to keep the pin exactly as it already is.
            if (!point.IsPoint || !mapping.PointAims.TryGetValue(point.Id, out var aim) || !aim.IsAimed)
                continue;

            var pointInSurface = point.P1 + subject.TracedToPinned;
            _pinFrom.Add(surfaceToOutput.TransformPoint(pointInSurface));
            _pinTargets.Add(aim.Position);
            _pinSurfacePositions.Add(pointInSurface);
            _pinPointIds.Add(point.Id);
        }

        if (_pinTargets.Count == 0)
            return false;

        if (_pinTargets.Count < 4)
        {
            mapping.Quad.AsSpan(0, 4).CopyTo(quad);
            return PointPinSolver.TrySolve(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinFrom),
                                           System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinTargets), quad, out _);
        }

        Span<int> outer = stackalloc int[4];
        if (!TryFindWidestFour(_pinSurfacePositions, outer))
            return false;

        Span<Vector2> outerFrom = stackalloc Vector2[4];
        Span<Vector2> outerTo = stackalloc Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            outerFrom[i] = _pinSurfacePositions[outer[i]];
            outerTo[i] = _pinTargets[outer[i]];
        }

        if (!Homography.TryComputeQuadToQuad(outerFrom, outerTo, out var outerSolve))
            return false;

        // The outer four go first, so the refined solve below reads them from the same lists.
        _pinSolveFrom.Clear();
        _pinSolveTo.Clear();
        for (var i = 0; i < 4; i++)
        {
            _pinSolveFrom.Add(outerFrom[i]);
            _pinSolveTo.Add(outerTo[i]);
        }

        var maxAgreeingMissPx = MaxAgreeingMissOfCanvasHeight * canvasSize.Y;
        for (var i = 0; i < _pinTargets.Count; i++)
        {
            if (outer.Contains(i))
                continue;

            var missPx = ((outerSolve.TransformPoint(_pinSurfacePositions[i]) - _pinTargets[i]) * canvasSize).Length();
            if (missPx > maxAgreeingMissPx)
            {
                _ignoredPointIds.Add(_pinPointIds[i]);
                continue;
            }

            _pinSolveFrom.Add(_pinSurfacePositions[i]);
            _pinSolveTo.Add(_pinTargets[i]);
        }

        var solve = outerSolve;
        if (_pinSolveFrom.Count > 4
            && (!Homography.TryComputeLeastSquares(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinSolveFrom),
                                                   System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinSolveTo), out solve)
                || !TryWritePin(surface, solve, mapping.Quad, quad)))
        {
            solve = outerSolve;
        }

        if (!TryWritePin(surface, solve, mapping.Quad, quad))
            return false;

        // The readout is in pixels — that is the unit an operator can judge a miss in.
        for (var i = 0; i < _pinSolveFrom.Count; i++)
        {
            var missed = (solve.TransformPoint(_pinSolveFrom[i]) - _pinSolveTo[i]) * canvasSize;
            _pinResidualPx = MathF.Max(_pinResidualPx, missed.Length());
        }

        return true;
    }

    /** The surface's corners carried through a solve, unless the result is not finite or folds the pin. */
    private static bool TryWritePin(Surface surface, in Homography surfaceToTargets, ReadOnlySpan<Vector2> currentQuad, Span<Vector2> quad)
    {
        Span<Vector2> rect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(surface, rect);
        for (var c = 0; c < 4; c++)
        {
            quad[c] = surfaceToTargets.TransformPoint(rect[c]);
            if (!float.IsFinite(quad[c].X) || !float.IsFinite(quad[c].Y))
                return false;
        }

        return IsUnfoldedLike(quad, currentQuad);
    }

    /// <summary>
    /// Whether a solved quad is convex and turns the same way as the pin it replaces. A solve whose horizon crosses
    /// the surface folds the quad, which is the "jump" to a second solution while dragging.
    /// </summary>
    private static bool IsUnfoldedLike(ReadOnlySpan<Vector2> quad, ReadOnlySpan<Vector2> currentQuad)
    {
        var turn = 0f;
        for (var c = 0; c < 4; c++)
        {
            var edge = quad[(c + 1) % 4] - quad[c];
            var next = quad[(c + 2) % 4] - quad[(c + 1) % 4];
            var cross = edge.X * next.Y - edge.Y * next.X;
            if (cross == 0 || (turn != 0 && MathF.Sign(cross) != MathF.Sign(turn)))
                return false;

            turn = cross;
        }

        var currentArea = SignedArea(currentQuad);
        return MathF.Abs(currentArea) < 1e-9f || MathF.Sign(currentArea) == MathF.Sign(turn);
    }

    private static float SignedArea(ReadOnlySpan<Vector2> quad)
    {
        var area = 0f;
        for (var c = 0; c < 4; c++)
        {
            var a = quad[c];
            var b = quad[(c + 1) % 4];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5f;
    }

    /// <summary>
    /// The four positions whose smallest triangle is largest. Three points in a near line are what leave a
    /// four-point perspective solve undetermined, so this picks the four that pin it down best. Depends only on
    /// where the points sit on the surface, never on where they were aimed, so it doesn't change during a drag.
    /// </summary>
    private static bool TryFindWidestFour(List<Vector2> positions, Span<int> best)
    {
        var bestSmallestTriangle = 0f;
        var count = positions.Count;
        for (var a = 0; a < count; a++)
        {
            for (var b = a + 1; b < count; b++)
            {
                for (var c = b + 1; c < count; c++)
                {
                    for (var d = c + 1; d < count; d++)
                    {
                        var smallest = MathF.Min(MathF.Min(TriangleArea(positions[a], positions[b], positions[c]),
                                                           TriangleArea(positions[a], positions[b], positions[d])),
                                                 MathF.Min(TriangleArea(positions[a], positions[c], positions[d]),
                                                           TriangleArea(positions[b], positions[c], positions[d])));
                        if (smallest <= bestSmallestTriangle)
                            continue;

                        bestSmallestTriangle = smallest;
                        best[0] = a;
                        best[1] = b;
                        best[2] = c;
                        best[3] = d;
                    }
                }
            }
        }

        return bestSmallestTriangle > 0;
    }

    private static float TriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        var ab = b - a;
        var ac = c - a;
        return MathF.Abs(ab.X * ac.Y - ab.Y * ac.X) * 0.5f;
    }

    /// <summary>
    /// The order the shoulder buttons walk the points in: the four spread furthest apart first, going round the
    /// wall clockwise, then the rest from the outside in. Aligning in this order sets the points that pin the
    /// perspective down before the ones that only refine it.
    /// </summary>
    private void WritePointCycleOrder(Surface traced, Vector2 tracedToPinned)
    {
        _cycleOrder.Clear();
        _cyclePositions.Clear();
        foreach (var annotation in traced.Annotations)
        {
            if (!annotation.IsPoint)
                continue;

            _cycleOrder.Add(annotation.Id);
            _cyclePositions.Add(annotation.P1 + tracedToPinned);
        }

        var count = _cycleOrder.Count;
        if (count < 2)
            return;

        Span<int> outer = stackalloc int[4];
        var outerCount = count >= 4 && TryFindWidestFour(_cyclePositions, outer) ? 4 : 0;

        var centre = Vector2.Zero;
        if (outerCount == 4)
        {
            for (var i = 0; i < 4; i++)
                centre += _cyclePositions[outer[i]] * 0.25f;
        }
        else
        {
            foreach (var position in _cyclePositions)
                centre += position / count;
        }

        // A sort key per point: outer points by their angle (clockwise from straight up, metres run Y up) in
        // [0, 2π), the rest after them by their distance from the centre, furthest first.
        _cycleKeys.Clear();
        for (var i = 0; i < count; i++)
        {
            var offset = _cyclePositions[i] - centre;
            var isOuter = outerCount != 4 || outer.Contains(i);
            if (isOuter)
            {
                var clockwiseFromUp = MathF.Atan2(offset.X, offset.Y);
                _cycleKeys.Add(clockwiseFromUp < 0 ? clockwiseFromUp + MathF.Tau : clockwiseFromUp);
            }
            else
            {
                _cycleKeys.Add(MathF.Tau + 1 / (1 + offset.Length()));
            }
        }

        // Insertion sort: a handful of points, sorted once per button press.
        for (var i = 1; i < count; i++)
        {
            for (var j = i; j > 0 && _cycleKeys[j - 1] > _cycleKeys[j]; j--)
            {
                (_cycleKeys[j - 1], _cycleKeys[j]) = (_cycleKeys[j], _cycleKeys[j - 1]);
                (_cycleOrder[j - 1], _cycleOrder[j]) = (_cycleOrder[j], _cycleOrder[j - 1]);
            }
        }
    }

    /// <summary>Canvas pixels a pin's bounding box must span before it is worth warping a photo through.</summary>
    private const float MinDiscWarpExtent = 8f;

    /** Whether "Calibrate" is on: the reference points are projected and can be dragged. */
    private bool _projectsPhoto;

    /** The point moved last, whose outline blinks on the wall; further moves and "Release Point" apply to it. */
    private Guid _activePointId;

    // Pin solve: the worst miss in pixels, and the scratch lists rebuilt per solve.
    private float _pinResidualPx;
    private readonly List<Vector2> _pinTargets = [];
    private readonly List<Vector2> _pinFrom = [];
    private readonly List<Vector2> _pinSurfacePositions = [];
    private readonly List<Guid> _pinPointIds = [];
    private readonly List<Vector2> _pinSolveFrom = [];
    private readonly List<Vector2> _pinSolveTo = [];

    /** Aimed points the last solve left out because the pin from the outer four misses them by too much. */
    private readonly HashSet<Guid> _ignoredPointIds = [];

    /** How far, in canvas heights, the pin from the outer four may miss a further point before that point is ignored. */
    private const float MaxAgreeingMissOfCanvasHeight = 0.02f;

    // Shoulder-button order: point ids, their positions on the surface and the keys they are sorted by.
    private readonly List<Guid> _cycleOrder = [];
    private readonly List<Vector2> _cyclePositions = [];
    private readonly List<float> _cycleKeys = [];
}
