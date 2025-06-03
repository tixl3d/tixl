using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;

// ReSharper disable SuggestVarOrType_SimpleTypes

namespace Lib.render.camera;

[Guid("6415ed0e-3692-45e2-8e70-fe0cf4d29ebc")]
internal sealed class OrbitCamera : Instance<OrbitCamera>
                         , ICameraPropertiesProvider, ICamera
{
    [Output(Guid = "14a63b62-5fbb-4f82-8cf3-d0faf279eff8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "451245E2-AC0B-435A-841E-7C9EDC804606", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Object> Reference = new();

    public OrbitCamera()
    {
        Output.UpdateAction += UpdateOutputWithSubtree;
        Reference.UpdateAction += UpdateCameraDefinition;
        Reference.Value = this;
    }

    private void UpdateOutputWithSubtree(EvaluationContext context)
    {
        if (!Reference.HasInputConnections || Reference.DirtyFlag.IsDirty)
            UpdateCameraDefinition(context);

        if (context.BypassCameras)
        {
            Command.GetValue(context);
            return;
        }

        // Set properties and evaluate sub tree
        var prevCameraToClipSpace = context.CameraToClipSpace;
        var prevWorldToCamera = context.WorldToCamera;

        context.CameraToClipSpace = CameraToClipSpace;
        context.WorldToCamera = WorldToCamera;

        Command.GetValue(context);

        context.CameraToClipSpace = prevCameraToClipSpace;
        context.WorldToCamera = prevWorldToCamera;
    }

    private void UpdateCameraDefinition(EvaluationContext context)
    {
        LastObjectToWorld = context.ObjectToWorld;
        var damping = Damping.GetValue(context).Clamp(0, 1);

        var isOverriding = OverrideTime.HasInputConnections;
        var time = isOverriding ? OverrideTime.GetValue(context) : context.LocalFxTime;

        var fov = MathUtils.ToRad * (FOV.GetValue(context));
        var aspectRatio = AspectRatio.GetValue(context);
        if (aspectRatio < 0.0001f)
        {
            aspectRatio = (float)context.RequestedResolution.Width / context.RequestedResolution.Height;
        }
            
        var positionOffset = PositionOffset.GetValue(context);

        Vector2 clip = NearFarClip.GetValue(context);

        CameraToClipSpace = GraphicsMath.PerspectiveFovRH(fov, aspectRatio, clip.X, clip.Y);

        var radiusValue = DistanceToTarget.GetValue(context) == 0.0f ? .0001f : DistanceToTarget.GetValue(context); // avoid 0 radius that causes issues
        Vector3 p = new Vector3(0, 0, radiusValue);

        var seed = Seed.GetValue(context);
        var wobbleSpeed = WobbleSpeed.GetValue(context);
        var wobbleComplexity = (int)WobbleComplexity.GetValue(context).Clamp(1, 8);

        var rotOffset = RotationOffset.GetValue(context);

        // Orbit rotation
        Vector3 t = CameraTargetPosition.GetValue(context);
        Vector3 center = new Vector3(t.X, t.Y, t.Z);

        var spinOffset = SpinOffset.GetValue(context);
        var orbitYaw = ComputeAngle(SpinAngleAndWobble, 1)
                       + MathUtils.ToRad * ((float)((SpinRate.GetValue(context) * time) * 360
                                                    + spinOffset
                                                    + MathUtils.PerlinNoise(0, 1, 6, seed) * 360));
        var orbitPitch = -ComputeAngle(OrbitAngleAndWobble, 2);
        var rot = Matrix4x4.CreateFromYawPitchRoll(
                                                   orbitYaw,
                                                   orbitPitch,
                                                   0);
        var p2 = Vector3.Transform(p, rot);
        var eye = new Vector3(p2.X, p2.Y, p2.Z);

        // View rotation
        var viewDirection = center - eye;

        var viewYaw = ComputeAngle(AimYawAngleAndWobble, 3) + rotOffset.X * MathUtils.ToRad;
        var viewPitch = ComputeAngle(AimPitchAngleAndWobble, 4) + rotOffset.Y * MathUtils.ToRad;
        var rotateAim = Matrix4x4.CreateFromYawPitchRoll(
                                                         viewYaw,
                                                         viewPitch,
                                                         0);

        var adjustedViewDirection = Vector3.TransformNormal(viewDirection, rotateAim);
        adjustedViewDirection = Vector3.Normalize(adjustedViewDirection);
            
            
            

        // Computing matrix
        var up = Up.GetValue(context);
        var roll = ComputeAngle(AimRollAngleAndWobble, 5) + rotOffset.Z * MathUtils.ToRad;

        var rotateAroundViewDirection = Matrix4x4.CreateFromAxisAngle(adjustedViewDirection, roll);
        up = Vector3.TransformNormal(up, rotateAroundViewDirection);
        up = Vector3.Normalize(up);
            
        eye+= Vector3.TransformNormal(positionOffset, rot);
        //eye+= positionOffset;
        var target = eye + adjustedViewDirection;

        _dampedEye = Vector3.Lerp(eye, _dampedEye, damping);
        _dampedTarget = Vector3.Lerp(target, _dampedTarget, damping);

        _cameraDefinition.Target = _dampedTarget;
        _cameraDefinition.Position = _dampedEye;
        _cameraDefinition.Roll = roll;
        _cameraDefinition.Up = up;
        _cameraDefinition.AspectRatio = aspectRatio;
        _cameraDefinition.ClipPlanes = clip;
        _cameraDefinition.FieldOfView = fov;
        _cameraDefinition.PositionOffset = positionOffset;
        _cameraDefinition.RotationOffset = rotOffset;

        WorldToCamera = GraphicsMath.LookAtRH(_dampedEye, _dampedTarget, up);

        CameraPosition = eye;
        CameraTarget = eye + adjustedViewDirection;

        float ComputeAngle(Slot<Vector2> angleAndWobbleInput, int seedIndex)
        {
            var angleAndWobble = angleAndWobbleInput.GetValue(context);
            var wobble = Math.Abs(angleAndWobble.Y) < 0.001f
                             ? 0
                             : (MathUtils.PerlinNoise((float)time * wobbleSpeed,
                                                      1,
                                                      wobbleComplexity,
                                                      seed + 123 * seedIndex)
                                - 0.5f) * 2 * angleAndWobble.Y;
            return MathUtils.ToRad * (angleAndWobble.X + wobble);
        }
    }

    public Matrix4x4 CameraToClipSpace { get; set; }
    public Vector3 CameraPosition { get; set; }
    public Vector3 CameraTarget { get; set; }
    public float CameraRoll { get; set; }
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 LastObjectToWorld { get; set; }

    private Vector3 _dampedTarget;
    private Vector3 _dampedEye;
    private CameraDefinition _cameraDefinition;
    public CameraDefinition CameraDefinition => _cameraDefinition;

    [Input(Guid = "33752356-8348-4938-8f73-6257e6bb1c1f")]
    public readonly InputSlot<Command> Command = new InputSlot<Command>();

    [Input(Guid = "ACF14901-3373-4B0C-8567-03EA0051A21F")]
    public readonly InputSlot<Vector3> CameraTargetPosition = new InputSlot<Vector3>();

    [Input(Guid = "21f595ad-0808-48f1-bdd8-118d1527944c")]
    public readonly InputSlot<float> FOV = new InputSlot<float>();

    [Input(Guid = "DD92FB0A-4B3E-4492-BF59-437D914A1AD3")]
    public readonly InputSlot<float> DistanceToTarget = new InputSlot<float>();

    [Input(Guid = "DF65E717-E2FD-4E4F-9E41-D6BCD3FE67F1")]
    public readonly InputSlot<float> SpinRate = new InputSlot<float>();

    [Input(Guid = "8C4DAB88-68CB-40CC-B576-CA3F3EA8461F")]
    public readonly InputSlot<float> SpinOffset = new InputSlot<float>();

    [Input(Guid = "8B75047F-03B7-4619-8869-2906E66731D1")]
    public readonly InputSlot<Vector2> OrbitAngleAndWobble = new InputSlot<Vector2>();

    [Input(Guid = "1AF76B2E-CFFE-4E3F-8793-C7A59D00430B")]
    public readonly InputSlot<Vector2> AimRollAngleAndWobble = new InputSlot<Vector2>();

    [Input(Guid = "4D2D2D2D-00BD-4DF9-B209-62F0C7926C38")]
    public readonly InputSlot<Vector2> AimPitchAngleAndWobble = new InputSlot<Vector2>();

    [Input(Guid = "066CD0E7-DE72-4E04-BF13-686CCC301C5A")]
    public readonly InputSlot<Vector2> AimYawAngleAndWobble = new InputSlot<Vector2>();

    [Input(Guid = "7412E22C-1F15-4471-883B-4FCD792146F7")]
    public readonly InputSlot<Vector2> SpinAngleAndWobble = new InputSlot<Vector2>();

    [Input(Guid = "B6BF6FE1-6733-46C0-A274-FAB2A950F606")]
    public readonly InputSlot<float> WobbleSpeed = new InputSlot<float>();

    [Input(Guid = "0AD10E15-06EF-4DDE-8EB3-ED7FE989C88E")]
    public readonly InputSlot<int> WobbleComplexity = new InputSlot<int>();

    [Input(Guid = "FB113FD1-3EDF-4DB6-9DFB-800C62070D69")]
    public readonly InputSlot<float> Damping = new InputSlot<float>();

    [Input(Guid = "DD81B2AA-3252-4130-8DEF-A5B399D3E283")]
    public readonly InputSlot<int> Seed = new InputSlot<int>();

    [Input(Guid = "353e2f08-a55f-48ce-ae65-53d5d081b6f0")]
    public readonly InputSlot<Vector2> NearFarClip = new InputSlot<Vector2>();

    [Input(Guid = "C81B91C6-2D06-4E3E-97BD-01D60F5F0F7D")]
    public readonly InputSlot<Vector3> RotationOffset = new InputSlot<Vector3>();
        
    [Input(Guid = "11B13DB2-3C91-4199-8245-50AF200E3A9D")]
    public readonly InputSlot<Vector3> PositionOffset = new InputSlot<Vector3>();
        
        
    [Input(Guid = "bd1bc8a5-72ce-42b0-8914-4f6e124a18ae")]
    public readonly InputSlot<float> AspectRatio = new InputSlot<float>();

    [Input(Guid = "f51b38d7-2380-457f-897d-2429b2ad6ac3")]
    public readonly InputSlot<Vector3> Up = new InputSlot<Vector3>();
        
    [Input(Guid = "46AA9CE1-EC54-43C5-AD5E-2CA679A254FD")]
    public readonly InputSlot<float> OverrideTime = new();

}