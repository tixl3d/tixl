using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.render.camera;

[Guid("9e27c32d-b187-4b7c-9761-0c5bb4ae3c45")]
internal sealed class CameraWithRotation : Instance<CameraWithRotation>, ICameraPropertiesProvider, ICamera
{
    [Output(Guid = "70395556-2008-43ec-a73d-b4b35ae8ce58")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "9e10954c-3f9e-4f99-8442-cdeeb0a765fc")]
    public readonly Slot<Object> Reference = new();

    public CameraWithRotation()
    {
        Output.UpdateAction = UpdateOutputWithSubtree;
        Reference.UpdateAction = UpdateCameraDefinition;
        Reference.Value = this;
    }

    private void UpdateOutputWithSubtree(EvaluationContext context)
    {
        if (!Reference.HasInputConnections || Reference.DirtyFlag.IsDirty)
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

        var position = Position.GetValue(context);

        var rotationMode = RotationMode.GetEnumValue<RotationModes>(context);
        var quaternion = RotationQuaternion.GetValue(context);

        var rotationFactor = RotationFactor.GetValue(context);
        var rotationOffset2 = RotationOffset2.GetValue(context);
        var eulerAngles = Rotation.GetValue(context) * rotationFactor + rotationOffset2;
            
        Matrix4x4 rotationMatrix = Matrix4x4.Identity;
        switch (rotationMode)
        {
            case RotationModes.Euler:
            {
                // This does not work because axis have to be applied in reverse order
                // rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(eulerAngles.Y.ToRadians(), // Yaw
                //                                                eulerAngles.X.ToRadians(), // Pitch (ups should have been X) 
                //                                                eulerAngles.Z.ToRadians()); // Roll
                    
                // From Quaternion.cs:
                //  Roll first, about axis the object is facing, then
                //  pitch upward, then yaw to face into the new heading
                var pitch = Matrix4x4.CreateRotationY(eulerAngles.X.ToRadians());   // Yaw: Rotation around Y-axis
                var heading = Matrix4x4.CreateRotationX(eulerAngles.Y.ToRadians()); // Pitch: Rotation around X-axis
                var roll = Matrix4x4.CreateRotationZ(eulerAngles.Z.ToRadians());  // Roll: Rotation around Z-axis
                rotationMatrix = heading * pitch * roll;
                break;
            }
            case RotationModes.Quaternion:
                rotationMatrix = Matrix4x4.CreateFromQuaternion(new Quaternion(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W));
                break;
        }
            
        var deltaFromPosition = Vector3.TransformNormal(Vector3.UnitZ, rotationMatrix);
        _target = position + deltaFromPosition;

        _camDef = new CameraDefinition
                      {
                          NearFarClip = ClipPlanes.GetValue(context),
                          LensShift = LensShift.GetValue(context),
                          PositionOffset = PositionOffset.GetValue(context),
                          Position = position,
                          Target = _target,
                          Up = Up.GetValue(context),
                          AspectRatio = aspectRatio,
                          FieldOfView = FOV.GetValue(context).ToRadians(),
                          Roll = eulerAngles.Z.ToRadians(),
                          RotationOffset = RotationOffset.GetValue(context),
                          OffsetAffectsTarget = AlsoOffsetTarget.GetValue(context)
                      };

        var camToClipSpace = GraphicsMath.PerspectiveFovRH(_camDef.FieldOfView, _camDef.AspectRatio, _camDef.NearFarClip.X, _camDef.NearFarClip.Y);
        camToClipSpace.M31 = _camDef.LensShift.X;
        camToClipSpace.M32 = _camDef.LensShift.Y;

        var translationMatrix = Matrix4x4.CreateTranslation(-position);
        var worldToCamera =    translationMatrix * rotationMatrix;
        CameraToClipSpace = camToClipSpace;
        WorldToCamera = worldToCamera;
    }
        
    private enum RotationModes
    {
        Euler,
        Quaternion,
    }

    private Vector3 _target;

    public float CameraRoll { get; set; }
    private CameraDefinition _camDef;

    public Matrix4x4 CameraToClipSpace { get; set; }
        
    public CameraDefinition CameraDefinition => _camDef;
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 LastObjectToWorld { get; set; }

    // Implement ICamera 
    public Vector3 CameraPosition { get { return Position.Value; } set { Animator.UpdateVector3InputValue(Position, value); } }
    public Vector3 CameraTarget { get => _target; set { Log.Warning("Can't set Rotation camera target"); } }
        
    [Input(Guid = "3e155fc2-9a11-46d1-8da5-36793140ae3f")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "6bd84cf7-e89f-4e48-84ca-63fe5bab6291")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "E5756357-6EDC-4C1C-BCEE-BD160EB2EC62", MappedType = typeof(RotationModes))]
    public readonly InputSlot<int> RotationMode = new();

    [Input(Guid = "D4087018-EB4B-4EFC-9387-4B1D2D81A395")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "7054CB41-0641-4C50-88A9-27EEA0472B52")]
    public readonly InputSlot<Vector3> RotationFactor = new();

    [Input(Guid = "0FE2745D-5A87-47E1-84D4-544E96DF42A7")]
    public readonly InputSlot<Vector3> RotationOffset2 = new();

    // [Input(Guid = "66fa647e-c0f9-495d-8074-64fe05cf1977")]
    // public readonly InputSlot<System.Numerics.Vector3> Target = new();

    [Input(Guid = "ddd02480-dcfd-4ae9-a057-ee56472bc0ec")]
    public readonly InputSlot<float> FOV = new();

    [Input(Guid = "28683A86-6CE3-44A9-8226-1F6CF157BDE4")]
    public readonly InputSlot<Vector4> RotationQuaternion = new();

    // [Input(Guid = "a3c2610a-527f-469b-8ee8-7e6c49493776")]
    // public readonly InputSlot<float> Roll = new();

    // --- offset

    [Input(Guid = "d386ca2c-6ffe-4871-a1f4-f713b383e892")]
    public readonly InputSlot<Vector3> PositionOffset = new();

    [Input(Guid = "55c59681-f50b-44bc-80bc-3039b73b1c00")]
    public readonly InputSlot<bool> AlsoOffsetTarget = new();

    [Input(Guid = "325b86e7-3d2a-4eeb-95d3-3138696a8a94")]
    public readonly InputSlot<Vector3> RotationOffset = new();

    [Input(Guid = "2ba9e2c0-f494-49aa-9030-172177f06eda")]
    public readonly InputSlot<Vector2> LensShift = new();

    // --- options

    [Input(Guid = "766a9e48-9f3b-4204-973c-751bb7180400")]
    public readonly InputSlot<Vector2> ClipPlanes = new();

    [Input(Guid = "d5761f3a-b2d0-435d-900c-3ff185373269")]
    public readonly InputSlot<float> AspectRatio = new();

    [Input(Guid = "1b672745-d0fd-4611-862f-f27f59d3d416")]
    public readonly InputSlot<Vector3> Up = new();
}