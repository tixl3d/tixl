using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;


namespace Lib.render.camera;

[Guid("954af16f-b37b-4e64-a965-4bec02b9179e")]
internal sealed class OrthographicCamera : Instance<OrthographicCamera>, ICamera, ICameraPropertiesProvider
{
    [Output(Guid = "93241f33-8a3e-4bba-8852-ca5d4d4523aa", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();
        
    [Output(Guid = "761245E2-AC0B-435A-841E-7C9EDC804606")]
    public readonly Slot<Object> Reference = new();

    public OrthographicCamera()
    {
        Output.UpdateAction += Update;
        Reference.Value = this;
    }

    private void Update(EvaluationContext context)
    {
        var aspectRatio = AspectRatio.GetValue(context);
        if (aspectRatio < 0.0001f)
        {
            aspectRatio = (float)context.RequestedResolution.Width / context.RequestedResolution.Height;
        }
        
        
        LastObjectToWorld = context.ObjectToWorld;
        
        var roll = Roll.GetValue(context);
        var rollRotation = Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, -roll * MathUtils.ToRad);
        
        Vector2 size = Stretch.GetValue(context) * Scale.GetValue(context) * new Vector2(aspectRatio,1);
        Vector2 clip = NearFarClip.GetValue(context);
        CameraToClipSpace = Matrix4x4.CreateOrthographic(size.X, size.Y, clip.X, clip.Y);
        
            
        var pos = Position.GetValue(context);
        Vector3 eye = new Vector3(pos.X, pos.Y, pos.Z);
        var t = Target.GetValue(context);
        Vector3 target = new Vector3(t.X, t.Y, t.Z);
        var u = Up.GetValue(context);
        Vector3 up = new Vector3(u.X, u.Y, u.Z);
        WorldToCamera =  GraphicsMath.LookAtRH(eye, target, up) * rollRotation;


        var prevCameraToClipSpace = context.CameraToClipSpace;
        context.CameraToClipSpace = CameraToClipSpace;

        var prevWorldToCamera = context.WorldToCamera;
        context.WorldToCamera = WorldToCamera;
        Command.GetValue(context);

        context.CameraToClipSpace = prevCameraToClipSpace;
        context.WorldToCamera = prevWorldToCamera;
    }

    
    #region Implement ICamera ----------------
    
    public Vector3 CameraPosition
    {
        get => Position.Value;
        set => Animator.UpdateVector3InputValue(Position, value);
    }

    public Vector3 CameraTarget
    {
        get => Target.Value;
        set => Animator.UpdateVector3InputValue(Target, value);
    }

    public float CameraRoll
    {
        get => Roll.Value;
        set => Animator.UpdateFloatInputValue(Roll, value);
    }

    public CameraDefinition CameraDefinition => new();  // Not implemented
        
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 LastObjectToWorld { get; set; }
    public Matrix4x4 CameraToClipSpace { get; set; }
    #endregion
    
    
    [Input(Guid = "4f5832eb-23a0-4cdf-8144-3537578e3e26")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "a0a28003-d6b5-4af5-9444-acf7af18ab4e")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "1399ce7f-9352-4976-b02e-7e7102b14db5")]
    public readonly InputSlot<Vector3> Target = new();

    [Input(Guid = "7b181495-ebd6-48c1-a866-b7b8337ef10d")]
    public readonly InputSlot<Vector3> Up = new();

    [Input(Guid = "e4761300-f383-4d4c-9aa0-7d7ab7997973")]
    public readonly InputSlot<float> Roll = new();

    [Input(Guid = "9326957b-bc25-4f89-a833-9b8bb415d8ef")]
    public readonly InputSlot<Vector2> NearFarClip = new();
    
    [Input(Guid = "6FC4F861-9D65-4AF1-AAFC-D90851F61D7F")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "9EA6EA91-69BB-49D4-8A01-DA75484BA207")]
    public readonly InputSlot<float> AspectRatio = new();

    [Input(Guid = "8042eb60-ca86-42b3-a338-d733c3cbb1fb")]
    public readonly InputSlot<Vector2> Stretch = new();
}