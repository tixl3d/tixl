#nullable enable
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Turns an output's aligned surface into a projector camera: where the projector stands, where it points and how
/// far it zooms, so [UseProjectorCam] renders 3D content that lands on objects in front of the wall, not only on it.
/// </summary>
internal static class ProjectorCalibration
{
    /// <summary>
    /// Solves the output's camera from the largest top-level surface corner-pinned to it, as one undo step. The message
    /// says what was solved or why it couldn't be.
    /// </summary>
    public static bool TrySolveFromAlignedSurface(Setup setup, OutputDefinition output, out string message)
    {
        var surface = FindLargestMappedSurface(setup, output, out var mapping);
        if (surface == null || mapping == null)
        {
            message = "Corner-pin a surface onto this output and calibrate it first. Surfaces that fill the output don't count.";
            return false;
        }

        var canvas = output.ResolvedResolution;
        var canvasSize = output.CanvasSize;
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, canvasSize, out var surfaceToPixels))
        {
            message = $"The pin of '{surface.Name}' is degenerate.";
            return false;
        }

        var camera = output.Camera ?? new OutputDefinition.ProjectorCamera();
        var placement = surface.Placement?.Pose ?? Pose.Identity;
        var fallbackFieldOfView = camera.ManualFovYDegrees * MathF.PI / 180;
        if (!ProjectorSolver.TrySolveFromPlane(surfaceToPixels, placement, camera.LensAxis, canvas, fallbackFieldOfView,
                                               out var pose, out var lens, out var isZoomEntered))
        {
            message = $"Can't solve from '{surface.Name}'.";
            return false;
        }

        CollectCorrespondences(surface, mapping, placement, canvasSize, surfaceToPixels, _correspondences);
        var residual = MeanMissPx(pose, lens, canvas, _correspondences);
        var hasDistance = TryGetDistanceToSurface(pose, surface, placement, out var distance);

        SetupUndo.RunUndoable("Solve projector camera", setup, () =>
                                                              {
                                                                  var solved = output.Camera ??= new OutputDefinition.ProjectorCamera();
                                                                  solved.Pose = pose;
                                                                  solved.Lens = lens;
                                                                  solved.ResidualPx = MathF.Max(residual, 0.001f);
                                                                  solved.SolvedFromSurfaceId = surface.Id;
                                                                  solved.IsZoomEntered = isZoomEntered;
                                                                  solved.CalibrationPoints = new List<CalibrationPoint>(_correspondences);

                                                                  // The fields show what was solved, so the side view draws it and stays editable.
                                                                  if (!isZoomEntered)
                                                                      solved.ManualFovYDegrees = lens.FieldOfViewY * 180 / MathF.PI;

                                                                  if (hasDistance)
                                                                      solved.ThrowDistance = distance;
                                                              });

        message = DescribeSolve(setup, output.Camera!);
        return true;
    }

    /// <summary>
    /// Keeps a solved camera on the pin it was solved from while that pin is being calibrated, so content rendered
    /// through [UseProjectorCam] moves with the points being aimed. The zoom stays as solved or entered: read from a
    /// half-aimed pin it would jump with every point. Only the pose follows, and only when the pin changed. No undo
    /// step of its own: calibrating snapshots the whole setup, the camera included, and an undone pin is followed
    /// back on the next frame.
    /// </summary>
    public static void FollowPin(OutputDefinition output, Surface surface, Surface.OutputMapping mapping)
    {
        if (output.Camera is not { Pose: not null, Lens: { } solvedLens } camera
            || camera.SolvedFromSurfaceId != surface.Id
            || mapping.IsFilling
            || mapping.Quad.Length < 4)
            return;

        if (_followedOutputId == output.Id && _followedSurfaceId == surface.Id && _followedLensAxis == camera.LensAxis
            && _followedQuad[0] == mapping.Quad[0] && _followedQuad[1] == mapping.Quad[1]
            && _followedQuad[2] == mapping.Quad[2] && _followedQuad[3] == mapping.Quad[3])
            return;

        _followedOutputId = output.Id;
        _followedSurfaceId = surface.Id;
        _followedLensAxis = camera.LensAxis;
        for (var i = 0; i < 4; i++)
            _followedQuad[i] = mapping.Quad[i];

        var canvas = output.ResolvedResolution;
        var canvasSize = output.CanvasSize;
        var placement = surface.Placement?.Pose ?? Pose.Identity;
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, canvasSize, out var surfaceToPixels)
            || !ProjectorSolver.TrySolvePoseFromPlane(surfaceToPixels, placement, camera.LensAxis, canvas, solvedLens.FieldOfViewY,
                                                      out var pose, out var lens))
            return; // a folded or degenerate pin mid-aim: keep the last good camera

        CollectCorrespondences(surface, mapping, placement, canvasSize, surfaceToPixels, _correspondences);
        camera.Pose = pose;
        camera.Lens = lens;
        camera.ResidualPx = MathF.Max(MeanMissPx(pose, lens, canvas, _correspondences), 0.001f);
        camera.CalibrationPoints.Clear();
        camera.CalibrationPoints.AddRange(_correspondences);
        if (TryGetDistanceToSurface(pose, surface, placement, out var distance))
            camera.ThrowDistance = distance;
    }

    /// <summary>Drops the solve, so [UseProjectorCam] falls back to the manual camera.</summary>
    public static void UseManualCamera(Setup setup, OutputDefinition output)
    {
        if (output.Camera == null)
            return;

        SetupUndo.RunUndoable("Use manual projector camera", setup, () =>
                                                                    {
                                                                        var camera = output.Camera!;
                                                                        camera.Pose = null;
                                                                        camera.Lens = null;
                                                                        camera.ResidualPx = 0;
                                                                        camera.SolvedFromSurfaceId = Guid.Empty;
                                                                        camera.IsZoomEntered = false;
                                                                        camera.CalibrationPoints.Clear();
                                                                    });
    }

    /// <summary>What the camera currently is, in a line for the parameter card.</summary>
    public static string DescribeSolve(Setup setup, OutputDefinition.ProjectorCamera camera)
    {
        if (camera.Pose == null || camera.Lens is not { } lens)
            return "Manual camera: not solved yet.";

        var source = setup.FindSurface(camera.SolvedFromSurfaceId)?.Name;
        var from = source != null ? $" from '{source}'" : string.Empty;
        var zoom = camera.IsZoomEntered ? "entered zoom, seen too squarely to measure" : $"{lens.FieldOfViewY * 180 / MathF.PI:0.#}° vertical";
        return $"Solved{from}: {zoom}, misses by {camera.ResidualPx:0.#} px.";
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
            if (surface.ParentId != Guid.Empty || surface.Kind != Surface.Kinds.Physical || surface.FindMapping(output.Id) is not { } mapping
                || mapping.IsFilling || mapping.Quad.Length < 4)
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
    private static void CollectCorrespondences(Surface surface, Surface.OutputMapping mapping, Pose placement, Vector2 canvasSize,
                                               in Homography surfaceToPixels, List<CalibrationPoint> correspondences)
    {
        correspondences.Clear();
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

        if (correspondences.Count > 0)
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

    private static readonly List<CalibrationPoint> _correspondences = [];
    private static readonly Vector2[] _followedQuad = new Vector2[4];
    private static Guid _followedOutputId;
    private static Guid _followedSurfaceId;
    private static Vector2 _followedLensAxis;
}
