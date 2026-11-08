#nullable enable
using T3.Core.Animation;
using T3.Core.Utils;
using T3.Graphics.Compat;

namespace Lib.point.sim;

/// <summary>
/// Flashes attributes onto a selection of points on trigger and lets them recover to the
/// connected points over time. Combines [PointSimulation], [SetPointAttributes] and [FilterPoints].
/// </summary>
[Guid("ca0e9c98-5830-4942-82b4-aaf36b8076fd")]
internal sealed class FlashPointAttributes : Instance<FlashPointAttributes>, IStatusProvider
{
    [Output(Guid = "8dfc1d2a-ce23-4e71-821d-5b487ad996c3", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> Result = new();

    public FlashPointAttributes()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var points = Points.GetValue(context);
        var wasTriggered = MathUtils.WasTriggered(TriggerFlash.GetValue(context), ref _triggerFlash);
        var reset = Reset.GetValue(context);

        if (points?.Srv == null || points.Srv.IsDisposed)
        {
            Result.Value = null;
            return;
        }

        // Repeated evaluations of the same frame (motion blur, cube faces) must not advance the simulation.
        if (Playback.FrameCount == _lastUpdateFrame && !wasTriggered && !reset)
            return;

        _lastUpdateFrame = Playback.FrameCount;

        var time = context.LocalFxTime;
        var deltaTime = (float)Math.Max(0, time - _lastTime);
        _lastTime = time;

        _mainShaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.ComputeShader>(ShaderPath, this, () => "main");
        _flashShaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.ComputeShader>(ShaderPath, this, () => "flash");

        var mainShader = _mainShaderResource.Value;
        var flashShader = _flashShaderResource.Value;
        if (mainShader == null || flashShader == null)
        {
            _lastErrorMessage = "FlashPointAttributes compute shader is not available";
            Result.Value = points;
            return;
        }

        var pointCount = points.Srv.Description.Buffer.ElementCount;
        if (pointCount != _allocatedPointCount)
        {
            _allocatedPointCount = pointCount;
            SetupBuffer(_resultBuffer, pointCount, Point.Stride);
            SetupBuffer(_flashedBuffer, pointCount, Point.Stride);
            SetupBuffer(_stateBuffer, pointCount, FlashStateStride);
            _needsReset = true;
        }

        if (reset)
            _flashCount = 0;

        var rotationAxis = RotationAxis.GetValue(context);
        rotationAxis = rotationAxis.LengthSquared() > 0.000001f ? Vector3.Normalize(rotationAxis) : Vector3.UnitY;

        var setMask = 0;
        if (SetPosition.GetValue(context)) setMask |= (int)Attributes.Position;
        if (SetRotation.GetValue(context)) setMask |= (int)Attributes.Rotation;
        if (SetScale.GetValue(context)) setMask |= (int)Attributes.Scale;
        if (SetFx1.GetValue(context)) setMask |= (int)Attributes.Fx1;
        if (SetFx2.GetValue(context)) setMask |= (int)Attributes.Fx2;
        if (SetColor.GetValue(context)) setMask |= (int)Attributes.Color;

        _floatParams = new FloatParams
                           {
                               Color = Color.GetValue(context),
                               Position = Position.GetValue(context),
                               Strength = Strength.GetValue(context),
                               RotationAxis = rotationAxis,
                               RotationAngle = RotationAngle.GetValue(context),
                               Scale = Scale.GetValue(context),
                               Fx1 = Fx1.GetValue(context),
                               Fx2 = Fx2.GetValue(context),
                               Duration = Duration.GetValue(context),
                               GainAndBias = GainAndBias.GetValue(context),
                               DeltaTime = deltaTime,
                               Scatter = Scatter.GetValue(context),
                               Step = Step.GetValue(context),
                           };

        var flashCount = Math.Max(0, Count.GetValue(context));

        _intParams = new IntParams
                         {
                             SetMask = setMask,
                             PositionMode = PositionMode.GetValue(context),
                             StrengthFactor = StrengthFactor.GetValue(context),
                             DecayMode = DecayMode.GetValue(context),
                             StartIndex = Index.GetValue(context),
                             Count = flashCount,

                             // Advancing the seed per flash makes scattered selections differ on each trigger.
                             Seed = Seed.GetValue(context) + _flashCount,
                             Reset = reset || _needsReset ? 1 : 0,
                         };

        _needsReset = false;

        ResourceManager.SetupConstBuffer(_floatParams, ref _floatParamsBuffer);
        ResourceManager.SetupConstBuffer(_intParams, ref _intParamsBuffer);

        var deviceContext = ResourceManager.Device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;

        csStage.SetConstantBuffer(0, _floatParamsBuffer);
        csStage.SetConstantBuffer(1, _intParamsBuffer);
        csStage.SetShaderResource(0, points.Srv);
        csStage.SetUnorderedAccessView(0, _resultBuffer.Uav);
        csStage.SetUnorderedAccessView(1, _flashedBuffer.Uav);
        csStage.SetUnorderedAccessView(2, _stateBuffer.Uav);

        csStage.Set(mainShader);
        deviceContext.Dispatch(pointCount / ThreadGroupSize + 1, 1, 1);

        if (wasTriggered && flashCount > 0 && setMask != 0 && _intParams.Reset == 0)
        {
            csStage.Set(flashShader);
            deviceContext.Dispatch(flashCount / ThreadGroupSize + 1, 1, 1);
            _flashCount++;
        }

        csStage.SetUnorderedAccessView(0, null);
        csStage.SetUnorderedAccessView(1, null);
        csStage.SetUnorderedAccessView(2, null);
        csStage.SetShaderResource(0, null);
        csStage.SetConstantBuffer(0, null);
        csStage.SetConstantBuffer(1, null);
        csStage.Set(null);

        Result.Value = _resultBuffer;
    }

    private static void SetupBuffer(BufferWithViews buffer, int elementCount, int stride)
    {
        ResourceManager.SetupStructuredBuffer(elementCount * stride, stride, ref buffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(buffer.Buffer, ref buffer.Srv);
        ResourceManager.CreateStructuredBufferUav(buffer.Buffer, UnorderedAccessViewBufferFlags.None, ref buffer.Uav);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _resultBuffer.Dispose();
        _flashedBuffer.Dispose();
        _stateBuffer.Dispose();
        GraphicsUtilities.Dispose(ref _floatParamsBuffer);
        GraphicsUtilities.Dispose(ref _intParamsBuffer);
    }

    #region status provider
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        return _lastErrorMessage == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    string IStatusProvider.GetStatusMessage()
    {
        return _lastErrorMessage ?? string.Empty;
    }
    #endregion

    [Flags]
    private enum Attributes
    {
        Position = 1,
        Rotation = 2,
        Scale = 4,
        Fx1 = 8,
        Fx2 = 16,
        Color = 32,
    }

    private enum PositionModes
    {
        Offset,
        Replace,
    }

    private enum StrengthFactors
    {
        None = 0,
        F1 = 1,
        F2 = 2,
    }

    private enum DecayModes
    {
        Curve,
        HalfLife,
    }

    [StructLayout(LayoutKind.Explicit, Size = 96)]
    private struct FloatParams
    {
        [FieldOffset(0)] public Vector4 Color;
        [FieldOffset(16)] public Vector3 Position;
        [FieldOffset(28)] public float Strength;
        [FieldOffset(32)] public Vector3 RotationAxis;
        [FieldOffset(44)] public float RotationAngle;
        [FieldOffset(48)] public Vector3 Scale;
        [FieldOffset(60)] public float Fx1;
        [FieldOffset(64)] public float Fx2;
        [FieldOffset(68)] public float Duration;
        [FieldOffset(72)] public Vector2 GainAndBias;
        [FieldOffset(80)] public float DeltaTime;
        [FieldOffset(84)] public float Scatter;
        [FieldOffset(88)] public float Step;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct IntParams
    {
        [FieldOffset(0)] public int SetMask;
        [FieldOffset(4)] public int PositionMode;
        [FieldOffset(8)] public int StrengthFactor;
        [FieldOffset(12)] public int DecayMode;
        [FieldOffset(16)] public int StartIndex;
        [FieldOffset(20)] public int Count;
        [FieldOffset(24)] public int Seed;
        [FieldOffset(28)] public int Reset;
    }

    private const string ShaderPath = "Lib:shaders/points/sim/FlashPointAttributes.hlsl";
    private const int ThreadGroupSize = 64;

    /** Age, attribute mask and padding of the per-point state in the shader. */
    private const int FlashStateStride = 16;

    private Resource<T3.Core.DataTypes.ComputeShader>? _mainShaderResource;
    private Resource<T3.Core.DataTypes.ComputeShader>? _flashShaderResource;
    private readonly BufferWithViews _resultBuffer = new();
    private readonly BufferWithViews _flashedBuffer = new();
    private readonly BufferWithViews _stateBuffer = new();
    private Buffer? _floatParamsBuffer;
    private Buffer? _intParamsBuffer;
    private FloatParams _floatParams;
    private IntParams _intParams;

    private int _allocatedPointCount = -1;
    private bool _needsReset = true;
    private bool _triggerFlash;
    private int _flashCount;
    private int _lastUpdateFrame = -1;
    private double _lastTime;
    private string? _lastErrorMessage;

    [Input(Guid = "14e19d37-c921-4a0d-9254-cda8870764c0")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "2d013020-13c9-412b-890b-6360ee3261b6")]
    public readonly InputSlot<bool> TriggerFlash = new();

    [Input(Guid = "864ef3df-4a5c-43cc-a4c6-420825b8852e")]
    public readonly InputSlot<int> Index = new();

    [Input(Guid = "5e7537fc-bbfb-416c-9f39-8d45d6e3c82e")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "21d2b49f-4a2c-431d-b6f8-1dbc49d5811c")]
    public readonly InputSlot<float> Step = new();

    [Input(Guid = "ac4a5f51-6080-4c15-8c9b-e5d2bbe3e47e")]
    public readonly InputSlot<float> Scatter = new();

    [Input(Guid = "229a9f10-ca06-4581-9bd8-a6ef3b881120")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "ca15d267-1340-47d3-b49c-d99c070ad0e8")]
    public readonly InputSlot<float> Strength = new();

    [Input(Guid = "d161f5ff-3c38-474d-8468-1dedc42d9cf8", MappedType = typeof(StrengthFactors))]
    public readonly InputSlot<int> StrengthFactor = new();

    [Input(Guid = "d84b9184-335f-4672-9d00-9f6eb946d13b")]
    public readonly InputSlot<bool> SetPosition = new();

    [Input(Guid = "00ab4d10-a25a-4645-b549-4798221da7d5")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "8e5269e6-0a01-49e0-983e-1ae100418f93", MappedType = typeof(PositionModes))]
    public readonly InputSlot<int> PositionMode = new();

    [Input(Guid = "3a743635-33e5-46f0-9469-57112a4b949d")]
    public readonly InputSlot<bool> SetRotation = new();

    [Input(Guid = "d7760cca-c53d-4024-b945-14a804db9598")]
    public readonly InputSlot<Vector3> RotationAxis = new();

    [Input(Guid = "fd27f41e-e47e-491f-b920-d5e7c158a632")]
    public readonly InputSlot<float> RotationAngle = new();

    [Input(Guid = "f72f0236-76e0-431b-bd17-40c379f4f9d9")]
    public readonly InputSlot<bool> SetScale = new();

    [Input(Guid = "b7dbf7fd-e521-4fba-b621-a34d934547f0")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "87c3c3c9-3376-435f-b723-ee029e739f00")]
    public readonly InputSlot<bool> SetFx1 = new();

    [Input(Guid = "ff2f9860-8a7e-4fbb-bc3a-9dc1be429976")]
    public readonly InputSlot<float> Fx1 = new();

    [Input(Guid = "4027a9c5-c925-4c1b-99c7-e5012f70270c")]
    public readonly InputSlot<bool> SetFx2 = new();

    [Input(Guid = "75796cae-7c64-4930-8606-ee5fda3d48d1")]
    public readonly InputSlot<float> Fx2 = new();

    [Input(Guid = "ca9de053-a43d-47cd-a112-aa7ee1fa1104")]
    public readonly InputSlot<bool> SetColor = new();

    [Input(Guid = "5f1751eb-b3b5-4ca8-b0c7-0f705c0cac29")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "0b48aec5-f0aa-4b47-8e24-20d647a81acb", MappedType = typeof(DecayModes))]
    public readonly InputSlot<int> DecayMode = new();

    [Input(Guid = "aacce3e6-affc-4e67-a5a2-874f54a92dde")]
    public readonly InputSlot<float> Duration = new();

    [Input(Guid = "1fc950b7-9a1a-49e5-a12c-46c08b0e3676")]
    public readonly InputSlot<Vector2> GainAndBias = new();

    [Input(Guid = "ab434153-e29f-417f-8cd6-2effbd065da5")]
    public readonly InputSlot<bool> Reset = new();
}
