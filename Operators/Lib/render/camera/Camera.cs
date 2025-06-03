using T3.Core.Rendering;
using T3.Core.Utils;

namespace Lib.render.camera;

[Guid("746d886c-5ab6-44b1-bb15-f3ce2fadf7e6")]
internal sealed class Camera : Instance<Camera>, ICamera, ICameraPropertiesProvider
{
    [Output(Guid = "2E1742D8-9BA3-4236-A0CD-A2B02C9F5924")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "761245E2-AC0B-435A-841E-7C9EDC804606")]
    public readonly Slot<Object> Reference = new();
 
    public Camera()
    {
        Output.UpdateAction += UpdateOutputWithSubtree;
        Reference.UpdateAction += UpdateCameraDefinition;
        Reference.Value = this;
    }

    private void UpdateOutputWithSubtree(EvaluationContext context)
    {
        if(!Reference.HasInputConnections || Reference.DirtyFlag.IsDirty) 
            UpdateCameraDefinition(context);
            
        Reference.DirtyFlag.Clear();
            
        if (context.BypassCameras)
        {
            Command.GetValue(context);
            return;
        }

        // Set properties and evaluate sub tree
        var prevWorldToCamera = context.WorldToCamera;
        var prevCameraToClipSpace = context.CameraToClipSpace;

        context.WorldToCamera = WorldToCamera;
        context.CameraToClipSpace = CameraToClipSpace;

        Command.GetValue(context);

        context.CameraToClipSpace = prevCameraToClipSpace;
        context.WorldToCamera = prevWorldToCamera;
    }
        
    private void UpdateCameraDefinition(EvaluationContext context)
    {
        LastObjectToWorld = context.ObjectToWorld;

        var aspectRatio = AspectRatio.GetValue(context);
        if (aspectRatio < 0.0001f)
        {
            aspectRatio = (float)context.RequestedResolution.Width / context.RequestedResolution.Height;
        }

        _cameraDefinition = new CameraDefinition
                                {
                                    ClipPlanes = ClipPlanes.GetValue(context),
                                    LensShift = LensShift.GetValue(context),
                                    PositionOffset = PositionOffset.GetValue(context),
                                    Position = Position.GetValue(context),
                                    Target = Target.GetValue(context),
                                    Up = Up.GetValue(context),
                                    AspectRatio = aspectRatio,
                                    FieldOfView = FieldOfView.GetValue(context).ToRadians(),
                                    Roll = Roll.GetValue(context),
                                    RotationOffset = RotationOffset.GetValue(context),
                                    OffsetAffectsTarget = AlsoOffsetTarget.GetValue(context)
                                };

            
        _cameraDefinition.BuildProjectionMatrices(out var camToClipSpace, out var worldToCamera);

        CameraToClipSpace = camToClipSpace;
        WorldToCamera = worldToCamera;
            

    }

    // public static void BuildProjectionMatrices(System.Numerics.Vector3 position, System.Numerics.Vector3 target, System.Numerics.Vector3 positionOffset, System.Numerics.Vector3 rotationOffset, float aspectRatio,
    //                                             Vector2 nearFarClip, Vector2 viewPortShift, bool offsetAffectsTarget, float fov, float roll, System.Numerics.Vector3 up,
    //                                             out Matrix camToClipSpace, out Matrix worldToCamera)
    // {
    //     camToClipSpace = Matrix.PerspectiveFovRH(fov, aspectRatio, nearFarClip.X, nearFarClip.Y);
    //     camToClipSpace.M31 = viewPortShift.X;
    //     camToClipSpace.M32 = viewPortShift.Y;
    //
    //     var eye = new System.Numerics.Vector3(position.X, position.Y, position.Z);
    //     if (!offsetAffectsTarget)
    //         eye += positionOffset.ToSharpDx();
    //
    //     var worldToCameraRoot = Matrix.LookAtRH(eye, target.ToSharpDx(), up.ToSharpDx());
    //     var rollRotation = Matrix.RotationAxis(new System.Numerics.Vector3(0, 0, 1), -(float)roll);
    //     var additionalTranslation = offsetAffectsTarget ? Matrix.Translation(positionOffset.X, positionOffset.Y, positionOffset.Z) : Matrix.Identity;
    //
    //     var additionalRotation = Matrix.RotationYawPitchRoll(MathUtil.DegreesToRadians(rotationOffset.Y),
    //                                                          MathUtil.DegreesToRadians(rotationOffset.X),
    //                                                          MathUtil.DegreesToRadians(rotationOffset.Z));
    //
    //     worldToCamera = worldToCameraRoot * rollRotation * additionalRotation * additionalTranslation;
    // }
        

    public  CameraDefinition CameraDefinition => _cameraDefinition;
    private CameraDefinition _cameraDefinition;
        
    public Matrix4x4 CameraToClipSpace { get; set; }
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 LastObjectToWorld { get; set; }

    // Implement ICamera 
    public Vector3 CameraPosition
    {
        get { return Position.Value;} 
        set { Animator.UpdateVector3InputValue(Position, value); }
    }

    public Vector3 CameraTarget
    {
        get { return Target.Value;} 
        set { Animator.UpdateVector3InputValue(Target, value); }
    }

    public float CameraRoll
    {
        get { return Roll.Value;} 
        set { Animator.UpdateFloatInputValue(Roll, value); }

    }


    [Input(Guid = "047B8FAE-468C-48A7-8F3A-5FAC8DD5B3C6")]
    public readonly InputSlot<Command> Command = new();
        
    [Input(Guid = "313596CC-3854-436B-89DA-5FD40164CE76")]
    public readonly InputSlot<Vector3> Position = new();
        
    [Input(Guid = "A7ACB25C-D60C-43A6-B1DF-2CD5C6E183F3")]
    public readonly InputSlot<Vector3> Target = new();
        
    [Input(Guid = "7BDE5A5A-CE82-4903-92FF-14E540A605F0")]
    public readonly InputSlot<float> FieldOfView = new();
        
    [Input(Guid = "764CA304-FC86-48A9-9C82-A04FAC7EADB2")]
    public readonly InputSlot<float> Roll = new();

    // --- offset
        
    [Input(Guid = "FEE19916-846F-491A-A2EE-1E7B1AC8E533")]
    public readonly InputSlot<Vector3> PositionOffset = new();

    [Input(Guid = "123396F0-62C4-43CD-8BE0-A661553D4783")]
    public readonly InputSlot<bool> AlsoOffsetTarget = new();
        
    [Input(Guid = "D4D0F046-297B-440A-AEF8-C2F0426EF4F5")]
    public readonly InputSlot<Vector3> RotationOffset = new();
        
    [Input(Guid = "AE275370-A684-42FB-AB7A-50E16D24082D")]
    public readonly InputSlot<Vector2> LensShift = new();
        
    // --- options
        
    [Input(Guid = "199D4CE0-AAB1-403A-AD42-216EF1061A0E")]
    public readonly InputSlot<Vector2> ClipPlanes = new();
        
    [Input(Guid = "F66E91A1-B991-48C3-A8C9-33BCAD0C2F6F")]
    public readonly InputSlot<float> AspectRatio = new();
        
    [Input(Guid = "E6DFBFB9-EFED-4C17-8860-9C1A1CA2FA38")]
    public readonly InputSlot<Vector3> Up = new();
        
        

}