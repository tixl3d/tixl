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
internal sealed class UseProjectorCam : Instance<UseProjectorCam>
{
    [Output(Guid = "c4f70b23-8d5e-4a69-9b82-3061e7c14a05")]
    public readonly Slot<Command> Output = new();

    public UseProjectorCam()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var output = ActiveSetup.TryFindOutput(OutputRef.GetValue(context));
        if (context.BypassCameras || output == null)
        {
            Command.GetValue(context);
            return;
        }

        var cam = output.Camera;
        var aspect = output.CanvasResolution.Height > 0
                         ? output.CanvasResolution.Width / (float)output.CanvasResolution.Height
                         : 1f;

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

    [Input(Guid = "d5081c34-7e6f-4b7a-ac93-417208d25b16")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "e6192d45-6f70-4c8b-bd04-528319e36c27")]
    public readonly InputSlot<Guid> OutputRef = new();
}
