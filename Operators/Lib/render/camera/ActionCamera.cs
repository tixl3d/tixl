using T3.Core.Animation;
using T3.Core.Rendering;
using T3.Core.Utils;

namespace Lib.render.camera;

[Guid("dfd4c912-4c4f-44b1-88aa-f27aa66b57af")]
internal sealed class ActionCamera : Instance<ActionCamera>, ICamera, ICameraPropertiesProvider
{
    [Output(Guid = "66784a52-d003-4c7d-b3cf-bdca4acb041e")]
    public readonly Slot<object> Reference = new();

    public ActionCamera()
    {
        Reference.UpdateAction += UpdateCameraDefinition;
        Reference.Value = this;
    }

    private void UpdateCameraDefinition(EvaluationContext context)
    {
        var time = Playback.RunTimeInSecs;
        var deltaTime = (float)(time - _lastUpdateTime);
        _lastUpdateTime = time;

        LastObjectToWorld = context.ObjectToWorld;

        if (!ReferenceCamera.HasInputConnections)
        {
            Log.Warning("Missing reference camera", this);
            return;
        }

        var referenceCam = ReferenceCamera.GetValue(context) as ICamera;
        if (referenceCam == null)
        {
            Log.Warning("Connected op is not a camera", this);
            return;
        }

        var speed = Speed.GetValue(context);
        var rotationSpeed = RotationSpeed.GetValue(context);

        var reset = MathUtils.WasTriggered(TriggerReset.GetValue(context), ref _triggerReset);
        var blend = BlendToReferenceCamera.GetValue(context) * deltaTime * 60;
        if (reset || !_initialized)
        {
            TriggerReset.SetTypedInputValue(false);
            _initialized = true;
            blend = 1;
        }

        _cameraDefinition = CameraDefinition.Blend(_cameraDefinition, referenceCam.CameraDefinition, blend.Clamp(0, 1));

        var forward = Forward.GetValue(context);
        var sideways = Sideways.GetValue(context);
        var upDown = UpDown.GetValue(context);

        var yaw = Yaw.GetValue(context);
        var pitch = Pitch.GetValue(context);
        var roll = Roll.GetValue(context);

        var viewVector = _cameraDefinition.Target - _cameraDefinition.Position;
        var viewDirection = Vector3.Normalize(viewVector);

        var rotYMatrix = Matrix4x4.CreateRotationY(-yaw * rotationSpeed * deltaTime);
        var newViewDirection = Vector3.TransformNormal(viewDirection, rotYMatrix);

        var side = Vector3.Cross(Vector3.UnitY, newViewDirection);
        var rotXMatrix = Matrix4x4.CreateFromAxisAngle(side, pitch * rotationSpeed * deltaTime);
        newViewDirection = Vector3.TransformNormal(newViewDirection, rotXMatrix);

        _cameraDefinition.Position += viewDirection * forward * speed * deltaTime;
        _cameraDefinition.Position += side * sideways * speed * deltaTime;
        _cameraDefinition.Position += Vector3.UnitY * upDown * speed * deltaTime;

        _cameraDefinition.Target = _cameraDefinition.Position + newViewDirection;
        _cameraDefinition.Roll += roll * speed * deltaTime;

        _cameraDefinition.BuildProjectionMatrices(out var camToClipSpace, out var worldToCamera);

        CameraToClipSpace = camToClipSpace;
        WorldToCamera = worldToCamera;
    }

    private bool _triggerReset;
    private double _lastUpdateTime;

    private bool _initialized;

    private CameraDefinition _cameraDefinition = new CameraDefinition();

    #region implement ICamera
    public CameraDefinition CameraDefinition => _cameraDefinition;

    public Matrix4x4 CameraToClipSpace { get; set; }
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 LastObjectToWorld { get; set; }

    public Vector3 CameraPosition { get => _cameraDefinition.Position; set { } }
    public Vector3 CameraTarget { get => _cameraDefinition.Target; set { } }
    public float CameraRoll { get => _cameraDefinition.Roll; set { } }
    #endregion

    [Input(Guid = "D46EE53D-887B-4E37-B5BA-4EE0BA50C711")]
    public readonly InputSlot<object> ReferenceCamera = new();

    [Input(Guid = "3857258A-CA2D-4DAD-B926-28530E8A25B0")]
    public readonly InputSlot<float> BlendToReferenceCamera = new();

    [Input(Guid = "8AC6C739-3741-4816-8BAD-57BD2A226E02")]
    public readonly InputSlot<bool> TriggerReset = new();

    [Input(Guid = "44ED03FC-3551-4519-8B4C-DFDC1DDD1713")]
    public readonly InputSlot<float> Speed = new();

    [Input(Guid = "A93AA4AC-487C-4441-B501-742CD38A5F5E")]
    public readonly InputSlot<float> Forward = new();

    [Input(Guid = "C5DADDD7-A832-40D1-AEC3-3886803A3899")]
    public readonly InputSlot<float> Sideways = new();

    [Input(Guid = "37BBAF47-5985-42D7-801E-0BF099036D5F")]
    public readonly InputSlot<float> UpDown = new();

    [Input(Guid = "B117D2DC-EB4C-408E-9AB7-909D17AC3069")]
    public readonly InputSlot<float> RotationSpeed = new();

    [Input(Guid = "773F0B8F-16D4-4302-B26D-43CF4D4ADCE8")]
    public readonly InputSlot<float> Yaw = new();

    [Input(Guid = "832CD0D8-66BE-4F45-9AEA-6E439BE5A285")]
    public readonly InputSlot<float> Pitch = new();

    [Input(Guid = "C529AF20-2E52-4A29-B558-CD518BDC539D")]
    public readonly InputSlot<float> Roll = new();
}