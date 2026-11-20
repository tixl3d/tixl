#nullable enable
using T3.Core.Output;
using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Math3d;

namespace Lib.render.output;

/// <summary>
/// Renders its content sub-graph through a setup output's projector camera, so 3D scene geometry
/// aligned to the physical stage lands correctly on that projector (Shape 2 — no corner-pin warp).
/// Until the editor has solved the camera from a surface corner-pinned to the output, it renders from straight
/// in front of the stage origin at the projector's throw distance, zoom and lens shift. Wire: [3D scene] → UseProjectorCam(Projector) → RenderTarget → SendToOutput(Projector).
/// Its Reference hands the same camera to ops that take a camera reference, such as post effects.
/// </summary>
[Guid("b3e6f1a2-9c4d-4e58-8a71-2f5c6d0b93e4")]
internal sealed class UseProjectorCam : Instance<UseProjectorCam>, IStatusProvider, ICamera, ICameraPropertiesProvider
{
    [Output(Guid = "c4f70b23-8d5e-4a69-9b82-3061e7c14a05")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "e7d390bf-0c73-4c73-8f1a-4cc9ba899372")]
    public readonly Slot<object> Reference = new();

    public UseProjectorCam()
    {
        Output.UpdateAction += Update;
        Reference.UpdateAction += UpdateCamera;
        Reference.Value = this;
    }

    private void Update(EvaluationContext context)
    {
        if (!Reference.HasInputConnections || Reference.DirtyFlag.IsDirty)
            UpdateCamera(context);

        Reference.DirtyFlag.Clear();
        if (context.BypassCameras || !_hasOutput)
        {
            Command.GetValue(context);
            return;
        }

        var prevWorldToCamera = context.WorldToCamera;
        var prevCameraToClipSpace = context.CameraToClipSpace;
        context.WorldToCamera = WorldToCamera;
        context.CameraToClipSpace = CameraToClipSpace;

        // The innermost camera claims the rendered image for the render op's bundle.
        var renderedCameraSerial = context.RenderPass.RenderedCameraSerial;
        Command.GetValue(context);
        context.RenderPass.RecordCamera(renderedCameraSerial, this);

        context.CameraToClipSpace = prevCameraToClipSpace;
        context.WorldToCamera = prevWorldToCamera;
    }

    /** The projector camera's matrices and definition; without an output, the camera already in the context. */
    private void UpdateCamera(EvaluationContext context)
    {
        LastObjectToWorld = context.ObjectToWorld;
        var output = ActiveSetup.FindOutput(OutputRef.GetValue(context));
        _hasOutput = output != null;
        if (output == null)
        {
            WorldToCamera = context.WorldToCamera;
            CameraToClipSpace = context.CameraToClipSpace;
            return;
        }

        var cam = output.Camera;

        // The resolved size, as the camera was solved at: an output following its display has no CanvasResolution of its own.
        var canvas = output.CanvasSize;
        var aspect = canvas.X / canvas.Y;

        Pose pose;
        Projection lens;
        if (cam?.Pose is { } solvedPose && cam.Lens is { } solvedLens)
        {
            pose = solvedPose;
            lens = solvedLens;
        }
        else
        {
            // A surface without a stage position sits at the origin, so this lands roughly on it before any solve.
            var throwDistance = cam?.ThrowDistance ?? OutputDefinition.ProjectorCamera.DefaultThrowDistance;
            pose = new Pose(new Vector3(0, 0, throwDistance), Quaternion.Identity);
            lens = Projection.CreatePerspective((cam?.ManualFovYDegrees ?? 45f).ToRadians(), cam?.LensAxis ?? Vector2.Zero);
        }

        var cameraToClipSpace = lens.GetMatrix(aspect);
        CameraJitter.ApplyFromContext(context, ref cameraToClipSpace);
        WorldToCamera = pose.ToViewMatrix();
        CameraToClipSpace = cameraToClipSpace;

        // Projection stores the lens shift with the opposite sign of a camera definition's.
        var forward = Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
        _cameraDefinition = new CameraDefinition
                                {
                                    NearFarClip = new Vector2(lens.NearZ, lens.FarZ),
                                    LensShift = -lens.LensShift,
                                    Position = pose.Position,
                                    Target = pose.Position + forward,
                                    Up = Vector3.Transform(Vector3.UnitY, pose.Orientation),
                                    AspectRatio = aspect,
                                    FieldOfView = lens.FieldOfViewY,
                                };
    }

    #region ICamera
    /** Solved by the output setup, so a camera op can't move it. */
    public Vector3 CameraPosition { get => _cameraDefinition.Position; set { } }

    public Vector3 CameraTarget { get => _cameraDefinition.Target; set { } }
    public float CameraRoll { get => 0; set { } }
    public CameraDefinition CameraDefinition => _cameraDefinition;
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 CameraToClipSpace { get; set; }
    public Matrix4x4 LastObjectToWorld { get; set; } = Matrix4x4.Identity;
    #endregion

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

    private CameraDefinition _cameraDefinition = new();
    private bool _hasOutput;
}
