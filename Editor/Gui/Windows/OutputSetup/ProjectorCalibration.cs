#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Editor.UiModel.ProjectHandling;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Keeps each projector's camera solved from the surfaces corner-pinned to it: where the projector stands, where it
/// points and how far it zooms, so [UseProjectorCam] renders 3D content that lands on objects in front of the wall,
/// not only on it. The largest pinned surface gives the starting solve; the aimed points of every pinned surface then
/// refine the pose together, since a single plane seen almost head-on can't tell the projector's height from its tilt.
/// </summary>
internal static class ProjectorCalibration
{
    /// <summary>
    /// Re-solves every projector camera that syncs to its surface and whose pin, lens shift, zoom or stage placement
    /// changed; run once per frame by the setup publication, so it doesn't depend on any window being open. While a
    /// pin is being aimed (see <see cref="ReportAiming"/>) only the pose follows, with the zoom kept: read from a
    /// half-aimed pin it would jump with every point. The full solve, zoom included, follows once aiming ends. No
    /// undo step of its own: the camera is derived from the pin, so an undone pin is followed back on the next frame.
    /// </summary>
    public static void SyncCameras(Setup setup)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == _lastSyncFrame)
            return;

        _lastSyncFrame = frame;
        var isAiming = _isAimingReported && frame - _aimingReportFrame <= 1;
        var isCameraChanged = false;
        var isPoseFollowed = false;
        foreach (var output in setup.Outputs)
        {
            if (output.Kind != OutputDefinition.Kinds.Projector || output.Camera is { SyncsToSurface: false })
                continue;

            if (!_syncStates.TryGetValue(output.Id, out var state))
            {
                state = new SyncState();
                _syncStates[output.Id] = state;
            }

            var surface = FindSyncSurface(setup, output, out var mapping);
            if (surface == null || mapping == null)
            {
                state.Forget();
                state.Failure = "Not solved yet: corner-pin a surface onto this output. Surfaces that fill the output don't count.";
                isCameraChanged |= output.Camera is { } unpinned && TryForgetSolve(unpinned);
                continue;
            }

            var pinSignature = ComputePinSignature(setup, output);
            var isChanged = !state.Matches(output, surface, mapping, pinSignature);
            if (!isChanged && (isAiming || !state.NeedsFullSolve))
                continue;

            var camera = output.Camera ??= new OutputDefinition.ProjectorCamera();
            var posesOnly = isAiming && camera is { Pose: not null, Lens: not null };
            var poseBefore = camera.Pose;
            var lensBefore = camera.Lens;
            state.Failure = TrySolve(setup, output, camera, surface, mapping, posesOnly, out var surfaceCount, out var failure) ? null : failure;
            state.NeedsFullSolve = posesOnly;
            state.SurfaceCount = surfaceCount;
            state.Remember(output, surface, mapping, pinSignature);

            isPoseFollowed |= posesOnly && state.Failure == null && !Nullable.Equals(poseBefore, camera.Pose);

            // Re-solving an unchanged pin, as after loading, gives the same camera and isn't worth a save.
            isCameraChanged |= !posesOnly && state.Failure == null && (!Nullable.Equals(poseBefore, camera.Pose) || !Nullable.Equals(lensBefore, camera.Lens));
        }

        // A drag is saved when it ends, before the full solve that follows it; this keeps the file in step for the Player.
        if (isCameraChanged)
        {
            OutputSetupHandling.SaveActive();
        }
        else if (isPoseFollowed)
        {
            // Saved when the aiming ends; until then operators reading the camera still need to follow it.
            OutputSetupHandling.NotifyLiveEdit();
        }
    }

    /// <summary>Called each frame by the view a pin is aimed in, before <see cref="SyncCameras"/> runs the next frame.</summary>
    public static void ReportAiming(bool isAiming)
    {
        _isAimingReported = isAiming;
        _aimingReportFrame = ImGui.GetFrameCount();
    }

    /// <summary>What the camera currently is, or why it can't be solved, in a line for the parameter card.</summary>
    public static string DescribeCamera(Setup setup, OutputDefinition output)
    {
        var camera = output.Camera;
        var syncs = camera?.SyncsToSurface ?? true;
        if (syncs && _syncStates.TryGetValue(output.Id, out var state) && state.Failure != null)
            return state.Failure;

        if (camera?.Pose == null || camera.Lens is not { } lens)
        {
            return syncs
                       ? "Not solved yet: corner-pin a surface onto this output."
                       : "Not solved: turn on Sync to Surface Calibration to solve it from the surface pinned to this output.";
        }

        var source = setup.FindSurface(camera.SolvedFromSurfaceId)?.Name;
        var surfaceCount = _syncStates.TryGetValue(output.Id, out var solved) ? solved.SurfaceCount : 1;
        var from = source == null ? string.Empty
                   : surfaceCount > 1 ? $" from '{source}' and {surfaceCount - 1} more surface{(surfaceCount > 2 ? "s" : string.Empty)}"
                   : $" from '{source}'";
        var zoom = camera.IsZoomEntered ? "entered zoom, the surface can't tell it" : $"{lens.FieldOfViewY * 180 / MathF.PI:0.#}° vertical";
        var frozen = syncs ? string.Empty : " Kept while sync is off.";
        var loose = syncs && surfaceCount == 1 ? " Height and tilt stay loose on one plane: pin a second surface at an angle." : string.Empty;
        return $"Solved{from}: {zoom}, misses by {camera.ResidualPx:0.#} px.{frozen}{loose}";
    }

    /// <summary>
    /// Solves the camera from <paramref name="surface"/>'s pin into <paramref name="camera"/>, then refines the pose on
    /// the aimed points of every surface pinned to the output: the full solve measures the zoom unless the surface is
    /// seen too squarely, the pose-only one keeps the camera's zoom.
    /// </summary>
    private static bool TrySolve(Setup setup, OutputDefinition output, OutputDefinition.ProjectorCamera camera, Surface surface,
                                 Surface.OutputMapping mapping, bool posesOnly, out int surfaceCount, out string failure)
    {
        surfaceCount = 0;
        var canvas = output.ResolvedResolution;
        var canvasSize = output.CanvasSize;
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, canvasSize, out var surfaceToPixels))
        {
            failure = $"The pin of '{surface.Name}' is degenerate.";
            return false;
        }

        var placement = surface.Placement?.Pose ?? Pose.Identity;
        Pose pose;
        Projection lens;
        var isZoomEntered = camera.IsZoomEntered;
        var enteredFovY = ClampToZoomRange(camera, camera.ManualFovYDegrees * MathF.PI / 180);
        var isSolved = posesOnly
                           ? ProjectorSolver.TrySolvePoseFromPlane(surfaceToPixels, placement, camera.LensAxis, canvas,
                                                                   camera.IsZoomEntered ? enteredFovY : ClampToZoomRange(camera, camera.Lens!.Value.FieldOfViewY),
                                                                   out pose, out lens)
                           : ProjectorSolver.TrySolveFromPlane(surfaceToPixels, placement, camera.LensAxis, canvas,
                                                               enteredFovY, out pose, out lens, out isZoomEntered);

        // Zoom and distance trade off almost freely on one plane, so a keystone read outside the lens's range is noise.
        if (isSolved && !posesOnly && !isZoomEntered && !IsInZoomRange(camera, lens.FieldOfViewY))
        {
            isZoomEntered = true;
            isSolved = ProjectorSolver.TrySolvePoseFromPlane(surfaceToPixels, placement, camera.LensAxis, canvas, enteredFovY, out pose, out lens);
        }

        if (!isSolved)
        {
            failure = $"Can't solve from '{surface.Name}'.";
            return false;
        }

        _correspondences.Clear();
        AddCorrespondences(surface, mapping, placement, canvasSize, surfaceToPixels, _correspondences);
        surfaceCount = 1;
        foreach (var other in setup.Surfaces)
        {
            // A surface never placed on the stage would join the solve at the stage origin and pull the pose with it.
            if (other.Id == surface.Id || other.Placement is not { } otherPlacement || !IsPinnedTopLevel(other, output, out var otherMapping)
                || !SurfaceGeometry.TryGetSurfaceToOutput(other, otherMapping, canvasSize, out var otherToPixels))
                continue;

            AddCorrespondences(other, otherMapping, otherPlacement.Pose, canvasSize, otherToPixels, _correspondences);
            surfaceCount++;
        }

        ProjectorPoseRefinement.Refine(_correspondences, lens, canvas, ref pose);
        camera.Pose = pose;
        camera.Lens = lens;
        camera.ResidualPx = MathF.Max(MeanMissPx(pose, lens, canvas, _correspondences), 0.001f);
        camera.SolvedFromSurfaceId = surface.Id;
        camera.IsZoomEntered = isZoomEntered;
        camera.CalibrationPoints.Clear();
        camera.CalibrationPoints.AddRange(_correspondences);

        // The fields show what was solved, so the side view draws it and stays editable.
        if (!posesOnly)
            camera.ManualFovYDegrees = lens.FieldOfViewY * 180 / MathF.PI;

        if (TryGetDistanceToSurface(pose, surface, placement, out var distance))
            camera.ThrowDistance = distance;

        failure = string.Empty;
        return true;
    }

    /** The field of view in radians, held within the lens's zoom range. */
    private static float ClampToZoomRange(OutputDefinition.ProjectorCamera camera, float fieldOfViewY)
    {
        var range = camera.ZoomRangeDegrees * (MathF.PI / 180);
        return Math.Clamp(fieldOfViewY, MathF.Min(range.X, range.Y), MathF.Max(range.X, range.Y));
    }

    private static bool IsInZoomRange(OutputDefinition.ProjectorCamera camera, float fieldOfViewY)
    {
        var range = camera.ZoomRangeDegrees * (MathF.PI / 180);
        return fieldOfViewY >= MathF.Min(range.X, range.Y) * (1 - ZoomRangeTolerance)
               && fieldOfViewY <= MathF.Max(range.X, range.Y) * (1 + ZoomRangeTolerance);
    }

    /** How far a measured zoom may lie outside the entered range, relative to it, before it counts as noise. */
    private const float ZoomRangeTolerance = 0.1f;

    /// <summary>
    /// Drops a solve whose pin is gone. A synced camera is derived from its pin, and [UseProjectorCam] renders through
    /// a kept pose and lens instead of the entered zoom and lens shift, so those fields would seem to do nothing.
    /// </summary>
    private static bool TryForgetSolve(OutputDefinition.ProjectorCamera camera)
    {
        if (camera.Pose == null && camera.Lens == null)
            return false;

        camera.Pose = null;
        camera.Lens = null;
        camera.ResidualPx = 0;
        camera.SolvedFromSurfaceId = Guid.Empty;
        camera.IsZoomEntered = false;
        camera.CalibrationPoints.Clear();
        return true;
    }

    /// <summary>The surface the camera was last solved from while it is still pinned here, else the largest pinned one.</summary>
    private static Surface? FindSyncSurface(Setup setup, OutputDefinition output, out Surface.OutputMapping? mapping)
    {
        if (output.Camera is { SolvedFromSurfaceId: var solvedFromId } && solvedFromId != Guid.Empty
            && setup.FindSurface(solvedFromId) is { } solvedFrom && IsPinnedTopLevel(solvedFrom, output, out mapping))
        {
            return solvedFrom;
        }

        return FindLargestMappedSurface(setup, output, out mapping);
    }

    /** Whether a top-level physical surface is corner-pinned (not filling) onto the output. */
    private static bool IsPinnedTopLevel(Surface surface, OutputDefinition output, [NotNullWhen(true)] out Surface.OutputMapping? mapping)
    {
        mapping = surface.FindMapping(output.Id);
        return surface.ParentId == Guid.Empty && surface.Kind == Surface.Kinds.Physical
               && mapping is { IsFilling: false, Quad.Length: >= 4 };
    }

    /** Distance along the projector's view to the surface's centre. */
    private static bool TryGetDistanceToSurface(Pose projector, Surface surface, Pose placement, out float distance)
    {
        SurfaceGeometry.LocalBounds(surface, out var min, out var max);
        var centre = System.Numerics.Vector3.Transform(new Vector3((min + max) * 0.5f, 0), placement.ToWorldMatrix());
        var forward = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, projector.Orientation);
        distance = System.Numerics.Vector3.Dot(centre - projector.Position, forward);
        return float.IsFinite(distance) && distance > 0.01f;
    }

    /** The top-level surface on this output covering the most of its canvas: the steadiest plane to solve from. */
    private static Surface? FindLargestMappedSurface(Setup setup, OutputDefinition output, out Surface.OutputMapping? bestMapping)
    {
        bestMapping = null;
        Surface? best = null;
        var bestArea = 0f;
        foreach (var surface in setup.Surfaces)
        {
            // A filling mapping is the whole canvas by definition, never aimed at anything, and as the largest quad
            // it would always win: a surface that carries a full-frame projector-camera render would solve a
            // nonsense camera.
            if (!IsPinnedTopLevel(surface, output, out var mapping))
                continue;

            var area = MathF.Abs(QuadArea(mapping.Quad));
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = surface;
            bestMapping = mapping;
        }

        return best;
    }

    private static float QuadArea(Vector2[] quad)
    {
        var area = 0f;
        for (var i = 0; i < 4; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % 4];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5f;
    }

    /// <summary>
    /// The stage points the solve should hit and where: the surface's aimed reference points, which are what the
    /// alignment was made against, or its corners when none was aimed.
    /// </summary>
    private static void AddCorrespondences(Surface surface, Surface.OutputMapping mapping, Pose placement, Vector2 canvasSize,
                                           in Homography surfaceToPixels, List<CalibrationPoint> correspondences)
    {
        var countBefore = correspondences.Count;
        var surfaceToStage = placement.ToWorldMatrix();
        foreach (var annotation in surface.Annotations)
        {
            if (!annotation.IsPoint || !mapping.PointAims.TryGetValue(annotation.Id, out var aim) || !aim.IsAimed)
                continue;

            correspondences.Add(new CalibrationPoint
                                    {
                                        StagePosition = System.Numerics.Vector3.Transform(new Vector3(annotation.P1, 0), surfaceToStage),
                                        OutputPixel = aim.Position * canvasSize,
                                    });
        }

        if (correspondences.Count > countBefore)
            return;

        Span<Vector2> corners = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(surface, corners);
        foreach (var corner in corners)
        {
            correspondences.Add(new CalibrationPoint
                                    {
                                        StagePosition = System.Numerics.Vector3.Transform(new Vector3(corner, 0), surfaceToStage),
                                        OutputPixel = surfaceToPixels.TransformPoint(corner),
                                    });
        }
    }

    /// <summary>
    /// A checksum of everything the solve reads from the pinned surfaces — which ones, where they stand, their pins,
    /// aims and reference points — so an edit to any of them, not only the main one, triggers a re-solve.
    /// </summary>
    private static int ComputePinSignature(Setup setup, OutputDefinition output)
    {
        var hash = new HashCode();
        foreach (var surface in setup.Surfaces)
        {
            if (!IsPinnedTopLevel(surface, output, out var mapping))
                continue;

            hash.Add(surface.Id);
            hash.Add(surface.Placement?.Pose ?? Pose.Identity);
            hash.Add(surface.SizeInMeters);
            hash.Add(surface.AnchorInMeters);
            foreach (var corner in mapping.Quad)
            {
                hash.Add(corner);
            }

            foreach (var annotation in surface.Annotations)
            {
                if (!annotation.IsPoint || !mapping.PointAims.TryGetValue(annotation.Id, out var aim))
                    continue;

                hash.Add(annotation.P1);
                hash.Add(aim.IsAimed);
                hash.Add(aim.Position);
            }
        }

        return hash.ToHashCode();
    }

    private static float MeanMissPx(Pose pose, Projection lens, Int2 canvas, List<CalibrationPoint> correspondences)
    {
        if (correspondences.Count == 0)
            return 0;

        var sum = 0f;
        foreach (var point in correspondences)
        {
            if (!ProjectorSolver.TryProjectToPixel(pose, lens, canvas, point.StagePosition, out var predicted))
                return float.PositiveInfinity;

            sum += (predicted - point.OutputPixel).Length();
        }

        return sum / correspondences.Count;
    }

    /// <summary>
    /// What a projector's camera was last solved from, so a frame with the same pin, lens shift and placement costs
    /// a comparison rather than a solve, and what came of it.
    /// </summary>
    private sealed class SyncState
    {
        public bool NeedsFullSolve;
        public string? Failure;

        /** How many pinned surfaces the last solve drew its points from. */
        public int SurfaceCount = 1;

        public bool Matches(OutputDefinition output, Surface surface, Surface.OutputMapping mapping, int pinSignature)
        {
            var camera = output.Camera;
            return _isKnown && _surfaceId == surface.Id && _pinSignature == pinSignature && _canvas == output.ResolvedResolution
                   && camera != null && _lensAxis == camera.LensAxis
                   && (!camera.IsZoomEntered || _enteredFovY == camera.ManualFovYDegrees)
                   && _placement.Equals(surface.Placement?.Pose ?? Pose.Identity)
                   && _quad[0] == mapping.Quad[0] && _quad[1] == mapping.Quad[1] && _quad[2] == mapping.Quad[2] && _quad[3] == mapping.Quad[3];
        }

        public void Remember(OutputDefinition output, Surface surface, Surface.OutputMapping mapping, int pinSignature)
        {
            var camera = output.Camera!;
            _isKnown = true;
            _pinSignature = pinSignature;
            _surfaceId = surface.Id;
            _canvas = output.ResolvedResolution;
            _lensAxis = camera.LensAxis;
            _enteredFovY = camera.ManualFovYDegrees;
            _placement = surface.Placement?.Pose ?? Pose.Identity;
            for (var corner = 0; corner < 4; corner++)
                _quad[corner] = mapping.Quad[corner];
        }

        public void Forget()
        {
            _isKnown = false;
            NeedsFullSolve = false;
        }

        private bool _isKnown;
        private Guid _surfaceId;
        private int _pinSignature;
        private Int2 _canvas;
        private Vector2 _lensAxis;
        private float _enteredFovY;
        private Pose _placement;
        private readonly Vector2[] _quad = new Vector2[4];
    }

    private static readonly Dictionary<Guid, SyncState> _syncStates = new();
    private static readonly List<CalibrationPoint> _correspondences = [];
    private static int _lastSyncFrame = -1;
    private static bool _isAimingReported;
    private static int _aimingReportFrame = -1;
}
