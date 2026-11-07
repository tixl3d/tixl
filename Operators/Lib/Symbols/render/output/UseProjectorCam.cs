using T3.Core.Output;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.render.output;

/// <summary>
/// Renders its content sub-graph through a setup output's projector camera, so 3D scene geometry
/// aligned to the physical stage lands correctly on that projector (Shape 2 — no corner-pin warp).
/// Uses the output's manual camera (position → target, vertical FOV) until a calibration solve
/// provides a real pose/lens. Wire: [3D scene] → UseProjectorCam(Projector) → RenderTarget → SendToOutput(Projector).
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

        // Prefer the calibration-solved pose/lens; fall back to the manual look-at until a solve exists.
        if (cam?.Pose is { } pose && cam.Lens is { } lens)
        {
            context.WorldToCamera = pose.ToViewMatrix();
            context.CameraToClipSpace = lens.GetMatrix(aspect);
        }
        else
        {
            var position = cam?.ManualPosition ?? new Vector3(0, 1, 3);
            var target = cam?.ManualTarget ?? Vector3.Zero;
            var fovY = (cam?.ManualFovYDegrees ?? 45f).ToRadians();
            context.WorldToCamera = GraphicsMath.LookAtRH(position, target, new Vector3(0, 1, 0));
            context.CameraToClipSpace = GraphicsMath.PerspectiveFovRH(fovY, aspect, 0.01f, 1000f);
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
                   ? "The output's camera isn't solved yet, so this renders through its manual camera. Solve it in the output's Projector section."
                   : null;
    }

    [Input(Guid = "d5081c34-7e6f-4b7a-ac93-417208d25b16")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "e6192d45-6f70-4c8b-bd04-528319e36c27")]
    public readonly InputSlot<Guid> OutputRef = new();
}
