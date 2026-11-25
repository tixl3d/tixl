#nullable enable
using System.Runtime.InteropServices;
using T3.Graphics.Compat;

namespace Lib.point.track;

/// <summary>
/// Reads the points of one frame from a frame-major track buffer, interpolating between frames.
/// </summary>
[Guid("1aa5c0f4-ff12-4ac0-aa49-d5d3d0bd2d9f")]
internal sealed class SampleTrack : Instance<SampleTrack>, IStatusProvider
{
    [Output(Guid = "bef5bc51-d338-43cc-9cf2-344dc1fe42e5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> Points = new();

    public SampleTrack()
    {
        Points.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var track = TrackBuffer.GetValue(context);
        var markerCount = MarkerCount.GetValue(context);
        var frame = Frame.GetValue(context) - FirstFrame.GetValue(context);

        _statusMessage = null;
        if (track?.Srv == null || track.Srv.IsDisposed || markerCount <= 0)
        {
            _statusMessage = "Connect a TrackBuffer and its MarkerCount, e.g. from [TrackMarkers].";
            Points.Value = null;
            return;
        }

        var trackPointCount = track.Srv.Description.Buffer.ElementCount;
        if (trackPointCount % markerCount != 0)
        {
            _statusMessage = $"MarkerCount {markerCount} doesn't divide the track's {trackPointCount} points.";
            Points.Value = null;
            return;
        }

        _shaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, this, () => "main");
        var shader = _shaderResource.Value;
        if (shader == null)
        {
            _statusMessage = "SampleTrack compute shader is not available.";
            Points.Value = null;
            return;
        }

        if (markerCount != _allocatedMarkerCount)
        {
            ResourceManager.SetupStructuredBuffer(markerCount * Point.Stride, Point.Stride, ref _result.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_result.Buffer, ref _result.Srv);
            ResourceManager.CreateStructuredBufferUav(_result.Buffer, UnorderedAccessViewBufferFlags.None, ref _result.Uav);
            _allocatedMarkerCount = markerCount;
        }

        var parameters = new Params
                             {
                                 Frame = frame,
                                 MarkerCount = markerCount,
                                 FrameCount = trackPointCount / markerCount,
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
        deviceContext.Dispatch((markerCount + 63) / 64, 1, 1);
        stage.SetUnorderedAccessView(0, null);
        stage.SetShaderResource(0, null);
        deviceContext.PopState();

        Points.Value = _result;
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

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Params
    {
        [FieldOffset(0)] public float Frame;
        [FieldOffset(4)] public int MarkerCount;
        [FieldOffset(8)] public int FrameCount;
    }

    private const string ShaderPath = "Lib:shaders/points/track/SampleTrack.hlsl";

    private Resource<ComputeShader>? _shaderResource;
    private readonly BufferWithViews _result = new();
    private Buffer? _paramsBuffer;
    private int _allocatedMarkerCount = -1;
    private string? _statusMessage;

    [Input(Guid = "d7960316-c9a3-4414-b6e5-7f4419d71320")]
    public readonly InputSlot<BufferWithViews> TrackBuffer = new();

    [Input(Guid = "e9252f0b-f389-456f-8696-7825f95d7920")]
    public readonly InputSlot<int> MarkerCount = new();

    [Input(Guid = "9fefd12d-f090-44f7-ad19-c45d4834b7e7")]
    public readonly InputSlot<float> Frame = new();

    [Input(Guid = "060ef64c-35f1-48ac-9029-af47a426ccab")]
    public readonly InputSlot<int> FirstFrame = new();
}
