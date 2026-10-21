#nullable enable
using T3.Core.Output;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The stage's origin is the floor plans' (0,0) on the ground, and their +Y is its front (stage −Z): what a
/// scene's own origin and forward land on. Moving or turning it re-expresses the whole setup in the new frame —
/// plans, surface poses, projector cameras and their calibration points — so the room and every calibration
/// stay physically where they were and only the coordinates change. Plan cards keep their place on the Board.
/// </summary>
internal static class StageOrigin
{
    /// <summary>
    /// Puts the origin at <paramref name="newOrigin"/> and turns its front towards <paramref name="newFront"/>,
    /// both in the current plan space (metres, X right, Y the current front).
    /// </summary>
    public static void Move(Setup setup, Vector2 newOrigin, Vector2 newFront)
    {
        var frontLength = newFront.Length();
        if (frontLength < 0.000001f)
            return;

        // The plan rotation taking newFront onto +Y. Read straight off the unit front, so quarter turns stay exact.
        var front = newFront / frontLength;
        var cos = front.Y;
        var sin = front.X;

        // Seen from above that turn is a rotation about stage +Y; plan Y runs along stage −Z.
        var stageTurn = new Matrix4x4(cos, 0, -sin, 0,
                                      0, 1, 0, 0,
                                      sin, 0, cos, 0,
                                      0, 0, 0, 1);
        var stageTurnRotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(stageTurn));
        var stageOrigin = FloorPlanSync.PlanToStage(Vector2.Zero, newOrigin);

        foreach (var plan in setup.FloorPlans)
        {
            for (var vertexIndex = 0; vertexIndex < plan.Vertices.Count; vertexIndex++)
            {
                var offset = plan.Vertices[vertexIndex] - newOrigin;
                plan.Vertices[vertexIndex] = new Vector2(cos * offset.X - sin * offset.Y, sin * offset.X + cos * offset.Y);
            }

            // The card is drawn from its (0,0), so it follows the origin and the room stays put on the Board.
            if (plan.BoardPlacement != null)
                plan.BoardPlacement.Position += newOrigin;
        }

        foreach (var surface in setup.Surfaces)
        {
            if (surface.Placement != null)
                surface.Placement.Pose = TransformPose(surface.Placement.Pose, stageOrigin, stageTurn, stageTurnRotation);
        }

        foreach (var output in setup.Outputs)
        {
            var camera = output.Camera;
            if (camera == null)
                continue;

            if (camera.Pose is { } pose)
                camera.Pose = TransformPose(pose, stageOrigin, stageTurn, stageTurnRotation);

            camera.ManualPosition = TransformPoint(camera.ManualPosition, stageOrigin, stageTurn);
            camera.ManualTarget = TransformPoint(camera.ManualTarget, stageOrigin, stageTurn);
            foreach (var point in camera.CalibrationPoints)
                point.StagePosition = TransformPoint(point.StagePosition, stageOrigin, stageTurn);
        }

        // Derived surfaces are re-derived from the moved plans; a turn that isn't a quarter turn changes a floor's bounding box.
        foreach (var plan in setup.FloorPlans)
            FloorPlanSync.Apply(setup, plan);
    }

    /// <summary>The middle of the plan's bounds, where a room's origin usually belongs.</summary>
    public static bool TryGetPlanCentre(FloorPlan plan, out Vector2 centre)
    {
        centre = Vector2.Zero;
        if (!plan.TryGetBounds(out var min, out var max))
            return false;

        centre = (min + max) * 0.5f;
        return true;
    }

    /// <summary>The direction from the origin towards a wall standing on the segment, so that wall ends up in front.</summary>
    public static Vector2 FrontFacingSegment(FloorPlan plan, int segment)
    {
        plan.GetSegment(segment, out var start, out var end);
        var direction = end - start;
        var length = direction.Length();
        if (length < 0.000001f)
            return Vector2.UnitY;

        direction /= length;

        // Walls face into the room (see FloorPlanSync), so facing the wall means looking against its normal.
        var facesLeft = !plan.IsClosed || plan.SignedAreaTwice() >= 0;
        var wallNormal = facesLeft ? new Vector2(-direction.Y, direction.X) : new Vector2(direction.Y, -direction.X);
        return -wallNormal;
    }

    private static Pose TransformPose(in Pose pose, Vector3 stageOrigin, in Matrix4x4 stageTurn, Quaternion stageTurnRotation)
    {
        return new Pose(TransformPoint(pose.Position, stageOrigin, stageTurn),
                        Quaternion.Normalize(Quaternion.Concatenate(pose.Orientation, stageTurnRotation)));
    }

    private static Vector3 TransformPoint(Vector3 point, Vector3 stageOrigin, in Matrix4x4 stageTurn)
    {
        return Vector3.Transform(point - stageOrigin, stageTurn);
    }
}
