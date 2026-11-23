#nullable enable
using System.Runtime.InteropServices;
using T3.Graphics.Compat;

namespace Lib.point.track;

/// <summary>
/// Solves the 2D transform that keeps tracked markers on their anchors and outputs it as a single point.
/// </summary>
[Guid("a9cd6b25-2f20-431a-8a0f-2e21b011099d")]
internal sealed class SolveTrackTransform : Instance<SolveTrackTransform>, IStatusProvider
{
    [Output(Guid = "a1aa6b7b-bd2b-41bd-88b1-b0191325101e", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> Transform = new();

    public SolveTrackTransform()
    {
        Transform.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var track = TrackBuffer.GetValue(context);
        var markerCount = MarkerCount.GetValue(context);
        var firstFrame = FirstFrame.GetValue(context);
        var frameIndex = FrameIndex.GetValue(context);
        var referenceFrame = ReferenceFrame.GetValue(context);

        _statusMessage = null;
        if (track?.Srv == null || track.Srv.IsDisposed || markerCount <= 0)
        {
            _statusMessage = "Connect a TrackBuffer and its MarkerCount, e.g. from [TrackMarkers].";
            Transform.Value = null;
            return;
        }

        var trackPointCount = track.Srv.Description.Buffer.ElementCount;
        if (trackPointCount % markerCount != 0)
        {
            _statusMessage = $"MarkerCount {markerCount} doesn't divide the track's {trackPointCount} points.";
            Transform.Value = null;
            return;
        }

        var slotCount = trackPointCount / markerCount;
        var referenceSlot = referenceFrame - firstFrame;
        if (referenceSlot < 0 || referenceSlot >= slotCount)
        {
            _statusMessage = slotCount == 1
                                 ? "TrackBuffer holds a single frame. Connect the TrackBuffer output of [TrackMarkers], not Points."
                                 : $"ReferenceFrame {referenceFrame} lies outside the track {firstFrame}–{firstFrame + slotCount - 1}.";
            Transform.Value = null;
            return;
        }

        if (markerCount > MaxSolveMarkers)
            _statusMessage = $"Only the first {MaxSolveMarkers} of {markerCount} markers are used.";

        _shaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, this, () => "main");
        var shader = _shaderResource.Value;
        if (shader == null)
        {
            _statusMessage = "SolveTrackTransform compute shader is not available.";
            Transform.Value = null;
            return;
        }

        if (_result.Buffer == null)
        {
            ResourceManager.SetupStructuredBuffer(Point.Stride, Point.Stride, ref _result.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_result.Buffer, ref _result.Srv);
            ResourceManager.CreateStructuredBufferUav(_result.Buffer, UnorderedAccessViewBufferFlags.None, ref _result.Uav);
        }

        var parameters = new Params
                             {
                                 ReferencePosition = ReferencePosition.GetValue(context),
                                 ReferenceAngle = ReferenceRotation.GetValue(context) * MathF.PI / 180f,
                                 ReferenceScale = ReferenceScale.GetValue(context),
                                 FocusPoint = FocusPoint.GetValue(context),
                                 FocusRadius = MathF.Max(0, FocusRadius.GetValue(context)),
                                 RejectOutliers = RejectOutliers.GetValue(context) ? 1 : 0,
                                 MarkerCount = markerCount,
                                 SlotCount = slotCount,
                                 CurrentSlot = frameIndex - firstFrame,
                                 ReferenceSlot = referenceSlot,
                                 Mode = Mode.GetValue(context),
                                 SmoothFrames = SmoothFrames.GetValue(context),
                                 AllowRotation = AllowRotation.GetValue(context) ? 1 : 0,
                                 AllowScale = AllowScale.GetValue(context) ? 1 : 0,
                             };
        if (_paramsBuffer == null)
        {
            ResourceManager.SetupConstBuffer(parameters, ref _paramsBuffer);
        }
        else
        {
            ResourceManager.UpdateConstBuffer(parameters, _paramsBuffer);
        }

        var deviceContext = ResourceManager.Device.ImmediateContext;
        var stage = deviceContext.ComputeShader;
        deviceContext.PushState(StateGroups.ComputeShader);
        stage.Set(shader);
        stage.SetConstantBuffer(0, _paramsBuffer);
        stage.SetShaderResource(0, track.Srv);
        stage.SetUnorderedAccessView(0, _result.Uav);
        deviceContext.Dispatch(1, 1, 1);
        stage.SetUnorderedAccessView(0, null);
        stage.SetShaderResource(0, null);
        deviceContext.PopState();

        Transform.Value = _result;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _result.Dispose();
        GraphicsUtilities.Dispose(ref _paramsBuffer);
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
        => _statusMessage == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _statusMessage ?? string.Empty;

    private enum Modes
    {
        Lock,
        Smooth,
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct Params
    {
        [FieldOffset(0)] public Vector2 ReferencePosition;
        [FieldOffset(8)] public float ReferenceAngle;
        [FieldOffset(12)] public float ReferenceScale;
        [FieldOffset(16)] public Vector2 FocusPoint;
        [FieldOffset(24)] public float FocusRadius;
        [FieldOffset(28)] public int RejectOutliers;
        [FieldOffset(32)] public int MarkerCount;
        [FieldOffset(36)] public int SlotCount;
        [FieldOffset(40)] public int CurrentSlot;
        [FieldOffset(44)] public int ReferenceSlot;
        [FieldOffset(48)] public int Mode;
        [FieldOffset(52)] public int SmoothFrames;
        [FieldOffset(56)] public int AllowRotation;
        [FieldOffset(60)] public int AllowScale;
    }

    private const string ShaderPath = "Lib:shaders/points/track/SolveTrackTransform.hlsl";

    /** Matches MAX_SOLVE_MARKERS in the shader, which keeps per-marker arrays in registers. */
    private const int MaxSolveMarkers = 64;

    private Resource<ComputeShader>? _shaderResource;
    private readonly BufferWithViews _result = new();
    private Buffer? _paramsBuffer;
    private string? _statusMessage;

    [Input(Guid = "ec6cf73b-2b92-4f4a-b5a9-5eed3567c5b2")]
    public readonly InputSlot<BufferWithViews> TrackBuffer = new();

    [Input(Guid = "528c9c13-5156-48e9-bb63-4c1c021c64f5")]
    public readonly InputSlot<int> MarkerCount = new();

    [Input(Guid = "d09fc392-83ce-4082-9f8f-ba2953f7ec2e")]
    public readonly InputSlot<int> FirstFrame = new();

    [Input(Guid = "dd31b7de-46fc-4d7c-ae51-dc93f23d2ae0")]
    public readonly InputSlot<int> FrameIndex = new();

    [Input(Guid = "8e669835-798c-4c9f-96c4-f6de357a64d0")]
    public readonly InputSlot<int> ReferenceFrame = new();

    [Input(Guid = "7f115321-1297-4caa-9b2d-762ba8462dfd")]
    public readonly InputSlot<Vector2> ReferencePosition = new();

    [Input(Guid = "6443bfd2-8245-424c-87cd-3812b04b4dfe")]
    public readonly InputSlot<float> ReferenceRotation = new();

    [Input(Guid = "2c95dbea-4e7f-4d6c-8ac2-4bd896461826")]
    public readonly InputSlot<float> ReferenceScale = new();

    [Input(Guid = "9f51487e-13f2-4fc3-8830-41a859cdbd4f", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "6abd8504-cccb-49e1-a051-e7f44900f270")]
    public readonly InputSlot<int> SmoothFrames = new();

    [Input(Guid = "31108d92-9069-41a9-aa65-1dc14abd5949")]
    public readonly InputSlot<bool> AllowRotation = new();

    [Input(Guid = "ad8d0120-cb49-4eba-802c-a7fc56b1a15e")]
    public readonly InputSlot<bool> AllowScale = new();

    [Input(Guid = "6c6e02f3-97f6-4f9d-b547-54ee42efae82")]
    public readonly InputSlot<Vector2> FocusPoint = new();

    [Input(Guid = "87d19f66-2f7d-4430-b87a-a1b55d57774a")]
    public readonly InputSlot<float> FocusRadius = new();

    [Input(Guid = "3f59928c-7b9c-4c56-a74f-10fc04aa31d3")]
    public readonly InputSlot<bool> RejectOutliers = new();
}
