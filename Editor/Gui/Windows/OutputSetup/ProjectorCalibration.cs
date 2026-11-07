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
    /// Solves the output's camera from the largest top-level surface mapped to it, as one undo step. The message
    /// says what was solved or why it couldn't be.
    /// </summary>
    public static bool TrySolveFromAlignedSurface(Setup setup, OutputDefinition output, out string message)
    {
        var surface = FindLargestMappedSurface(setup, output, out var mapping);
        if (surface == null || mapping == null)
        {
            message = "Map a surface onto this output and align it first.";
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

        SetupUndo.RunUndoable("Solve projector camera", setup, () =>
                                                              {
                                                                  var solved = output.Camera ??= new OutputDefinition.ProjectorCamera();
                                                                  solved.Pose = pose;
                                                                  solved.Lens = lens;
                                                                  solved.ResidualPx = MathF.Max(residual, 0.001f);
                                                                  solved.SolvedFromSurfaceId = surface.Id;
                                                                  solved.IsZoomEntered = isZoomEntered;
                                                                  solved.CalibrationPoints = new List<CalibrationPoint>(_correspondences);
                                                              });

        message = DescribeSolve(setup, output.Camera!);
        return true;
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

    /** The top-level surface on this output covering the most of its canvas: the steadiest plane to solve from. */
    private static Surface? FindLargestMappedSurface(Setup setup, OutputDefinition output, out Surface.OutputMapping? bestMapping)
    {
        bestMapping = null;
        Surface? best = null;
        var bestArea = 0f;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId != Guid.Empty || surface.Kind != Surface.Kinds.Physical || surface.FindMapping(output.Id) is not { } mapping
                || mapping.Quad.Length < 4)
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
}
