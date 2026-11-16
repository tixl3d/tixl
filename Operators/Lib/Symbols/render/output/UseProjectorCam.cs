#nullable enable
using T3.Core.Output;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.render.output;

/// <summary>
/// Renders its content sub-graph through a setup output's projector camera, so 3D scene geometry
/// aligned to the physical stage lands correctly on that projector (Shape 2 — no corner-pin warp).
/// Until the editor has solved the camera from a surface corner-pinned to the output, it renders from straight
/// in front of the stage origin at the projector's throw distance, zoom and lens shift. Wire: [3D scene] → UseProjectorCam(Projector) → RenderTarget → SendToOutput(Projector).
/// </summary>
[Guid("b3e6f1a2-9c4d-4e58-8a71-2f5c6d0b93e4")]
internal sealed class UseProjectorCam : Instance<UseProjectorCam>, IStatusProvider
{
    [Output(Guid = "c4f70b23-8d5e-4a69-9b82-3061e7c14a05")]
    public readonly Slot<Command> Output = new();

    public UseProjectorCam()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var output = ActiveSetup.FindOutput(OutputRef.GetValue(context));
        if (context.BypassCameras || output == null)
        {
            Command.GetValue(context);
            return;
        }

        var cam = output.Camera;

        // The resolved size, as the camera was solved at: an output following its display has no CanvasResolution of its own.
        var canvas = output.CanvasSize;
        var aspect = canvas.X / canvas.Y;

        var prevWorldToCamera = context.WorldToCamera;
        var prevCameraToClipSpace = context.CameraToClipSpace;

        if (cam?.Pose is { } pose && cam.Lens is { } lens)
        {
            context.WorldToCamera = pose.ToViewMatrix();
            context.CameraToClipSpace = lens.GetMatrix(aspect);
        }
        else
        {
            // A surface without a stage position sits at the origin, so this lands roughly on it before any solve.
            var throwDistance = cam?.ThrowDistance ?? OutputDefinition.ProjectorCamera.DefaultThrowDistance;
            var unsolvedPose = new Pose(new Vector3(0, 0, throwDistance), Quaternion.Identity);
            var unsolvedLens = Projection.CreatePerspective((cam?.ManualFovYDegrees ?? 45f).ToRadians(), cam?.LensAxis ?? Vector2.Zero);
            context.WorldToCamera = unsolvedPose.ToViewMatrix();
            context.CameraToClipSpace = unsolvedLens.GetMatrix(aspect);
        }

        Command.GetValue(context);

        context.CameraToClipSpace = prevCameraToClipSpace;
        context.WorldToCamera = prevWorldToCamera;
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        GetStatusMessage() == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string? IStatusProvider.GetStatusMessage() => GetStatusMessage();

    /** Why the render isn't through a solved projector camera, or null when it is. Read from the last value: status is drawn outside evaluation. */
    private string? GetStatusMessage()
    {
        var outputId = OutputRef.Value;
        if (outputId == Guid.Empty)
            return "Pick the projector's output.";

        if (ActiveSetup.FindOutput(outputId) is not { } output)
            return "Output Ref doesn't name an output of the active setup, so this renders without a projector camera.";

        return output.Camera?.Pose == null
                   ? "The projector camera isn't solved yet, so this renders from straight in front of the stage origin. "
                     + "Corner-pin a surface onto this projector output, with Sync to Surface Calibration on in its Projector section."
                   : null;
    }

    [Input(Guid = "d5081c34-7e6f-4b7a-ac93-417208d25b16")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "e6192d45-6f70-4c8b-bd04-528319e36c27")]
    public readonly InputSlot<Guid> OutputRef = new();
}
