#nullable enable
using System.Runtime.InteropServices;
using T3.Core.Utils;
using T3.Graphics.Compat;

namespace Lib.point.track;

/// <summary>
/// Tracks hand-placed image features through a video on the GPU and records one point per marker and frame.
/// </summary>
[Guid("39658ad0-96cf-49c3-9b38-b8c1c32f2a6b")]
internal sealed class TrackMarkers : Instance<TrackMarkers>, IStatusProvider
{
    [Output(Guid = "8562f734-17e1-400a-99a4-7a474bb98758", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> Points = new();

    [Output(Guid = "15feac5c-dcfc-4d29-85ef-31d2017658e7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> TrackBuffer = new();

    [Output(Guid = "e98c4ce0-c6df-42b4-9009-431710e3e1aa", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> SeedQuality = new();

    [Output(Guid = "2f2231d1-12c3-4a07-84d6-ad205f897087", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> MarkerCount = new();

    [Output(Guid = "9916fcc6-4ab9-4e75-b1b9-d2702c1b8d71", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> TrackedFrameCount = new();

    public TrackMarkers()
    {
        Points.UpdateAction += Update;
        TrackBuffer.UpdateAction += Update;
        SeedQuality.UpdateAction += Update;
        MarkerCount.UpdateAction += Update;
        TrackedFrameCount.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // Read before GetValue clears it: editing a seed point must restart the track.
        var seedsChanged = SeedPoints.DirtyFlag.IsDirty;

        var image = Image.GetValue(context);
        var frameIndex = FrameIndex.GetValue(context);
        var seeds = SeedPoints.GetValue(context);
        var seedFrame = SeedFrame.GetValue(context);
        var firstFrame = FirstFrame.GetValue(context);
        var frameCount = FrameCount.GetValue(context).Clamp(1, MaxFrameCount);
        var patchSize = PatchSize.GetValue(context).Clamp(MinPatchSize, MaxPatchSize) | 1;
        var searchRadius = MathF.Max(1, SearchRadius.GetValue(context));
        var minConfidence = MinConfidence.GetValue(context);
        var reset = MathUtils.WasTriggered(Reset.GetValue(context), ref _resetTriggered);
        var isEnabled = IsEnabled.GetValue(context);

        _statusMessage = null;
        TrackFrame(image, frameIndex, seeds, seedFrame, firstFrame, frameCount, patchSize, searchRadius, minConfidence,
                   reset || seedsChanged, isEnabled);

        var isSetUp = _layout.MarkerCount > 0;
        Points.Value = isSetUp ? _currentPoints : null;
        TrackBuffer.Value = isSetUp ? _trackPoints : null;
        SeedQuality.Value = isSetUp ? _seedQuality : null;
        MarkerCount.Value = _layout.MarkerCount;
        TrackedFrameCount.Value = _trackedFrameCount;

        Points.DirtyFlag.Clear();
        TrackBuffer.DirtyFlag.Clear();
        SeedQuality.DirtyFlag.Clear();
        MarkerCount.DirtyFlag.Clear();
        TrackedFrameCount.DirtyFlag.Clear();
    }

    private void TrackFrame(Texture2D? image, int frameIndex, BufferWithViews? seeds, int seedFrame, int firstFrame,
                            int frameCount, int patchSize, float searchRadius, float minConfidence, bool restart,
                            bool isEnabled)
    {
        if (!isEnabled)
            return;

        if (image == null || image.IsDisposed || seeds?.Srv == null || seeds.Srv.IsDisposed)
        {
            _statusMessage = "Connect an Image and SeedPoints.";
            return;
        }

        var markerCount = seeds.Srv.Description.Buffer.ElementCount;
        if (markerCount <= 0)
        {
            _statusMessage = "SeedPoints is empty.";
            return;
        }

        if ((long)markerCount * frameCount > MaxTrackPointCount)
        {
            _statusMessage = $"{markerCount} markers × {frameCount} frames exceeds the track buffer limit. Reduce FrameCount.";
            return;
        }

        var seedSlot = seedFrame - firstFrame;
        if (seedSlot < 0 || seedSlot >= frameCount)
        {
            _statusMessage = $"SeedFrame {seedFrame} lies outside the tracked range {firstFrame}–{firstFrame + frameCount - 1}.";
            return;
        }

        _shaderResources ??= new ShaderResources(this);
        if (_shaderResources.Luma.Value == null || _shaderResources.Clear.Value == null || _shaderResources.Track.Value == null)
        {
            _statusMessage = "TrackMarkers compute shader is not available.";
            return;
        }

        var layout = new TrackLayout(markerCount, frameCount, firstFrame, seedFrame, patchSize,
                                     new Int2(image.Description.Width, image.Description.Height));
        if (layout != _layout || restart)
        {
            if (layout != _layout && !SetupResources(layout))
                return;

            ClearTrack();
        }

        // Repeated evaluations of one frame (motion blur, cube faces) must not track again.
        if (frameIndex == _lastFrameIndex && !_needsFrameUpdate)
            return;

        _lastFrameIndex = frameIndex;
        _needsFrameUpdate = false;

        var context = ResourceManager.Device.ImmediateContext;
        context.PushState(StateGroups.ComputeShader);
        UpdateLumaPyramid(context, image);

        var parameters = new Params
                             {
                                 ImageSize = new Vector2(layout.ImageSize.Width, layout.ImageSize.Height),
                                 MinConfidence = minConfidence,
                                 QualityScale = 1f / GradientRmsForFullQuality,
                                 MarkerCount = markerCount,
                                 PatchSize = patchSize,
                                 Levels = LevelsForSearchRadius(searchRadius, patchSize),
                                 TemplateLevels = TemplateLevelCount,
                                 Mode = (int)Modes.Hide,
                                 SourceSlot = -1,
                                 PreviousSlot = -1,
                                 TargetSlot = -1,
                                 IterationsPerLevel = IterationsPerLevel,
                             };

        var slot = frameIndex - firstFrame;
        if (slot < 0 || slot >= frameCount)
        {
            _statusMessage = $"Frame {frameIndex} lies outside the tracked range {firstFrame}–{firstFrame + frameCount - 1}.";
        }
        else if (!_isSeeded)
        {
            if (frameIndex == seedFrame)
            {
                parameters.Mode = (int)Modes.Capture;
                parameters.TargetSlot = slot;
                _isSeeded = true;
                MarkFilled(slot);
                _recentTemplateSlot = slot;
            }
            else
            {
                _statusMessage = $"Go to SeedFrame {seedFrame} to start tracking.";
            }
        }
        else if (_filledSlots[slot])
        {
            parameters.Mode = (int)Modes.Refresh;
            parameters.SourceSlot = slot;
            parameters.TargetSlot = slot;
            _recentTemplateSlot = slot;
        }
        else
        {
            var sourceSlot = FindNearestFilledSlot(slot);
            if (sourceSlot < 0 || Math.Abs(slot - sourceSlot) > MaxTrackingGapFrames)
            {
                _statusMessage = "Play or step from a tracked frame to continue tracking.";
            }
            else
            {
                var direction = Math.Sign(slot - sourceSlot);
                var isAdjacent = Math.Abs(slot - sourceSlot) == 1;
                var previousSlot = sourceSlot - direction;

                parameters.Mode = (int)Modes.Track;
                parameters.SourceSlot = sourceSlot;
                parameters.TargetSlot = slot;
                parameters.PreviousSlot = isAdjacent && IsFilled(previousSlot) ? previousSlot : -1;
                parameters.UseRecentTemplate = _recentTemplateSlot == sourceSlot ? 1 : 0;

                // Motion across a gap is unknown, so search one pyramid level wider.
                if (!isAdjacent)
                    parameters.Levels = Math.Min(parameters.Levels + 1, TemplateLevelCount);

                MarkFilled(slot);
                _recentTemplateSlot = slot;
            }
        }

        DispatchTrack(context, parameters, image, seeds);
        context.PopState();
    }

    private void UpdateLumaPyramid(DeviceContext context, Texture2D image)
    {
        var stage = context.ComputeShader;
        _lumaParams.ImageSize = new Vector2(_layout.ImageSize.Width, _layout.ImageSize.Height);
        UploadParams(_lumaParams);

        stage.Set(_shaderResources!.Luma.Value);
        stage.SetConstantBuffer(0, _paramsBuffer);
        stage.SetShaderResource(0, SrvManager.GetSrvForTexture(image));
        stage.SetUnorderedAccessView(5, _lumaUav);
        context.Dispatch((_layout.ImageSize.Width + 7) / 8, (_layout.ImageSize.Height + 7) / 8, 1);
        stage.SetUnorderedAccessView(5, null);
        stage.SetShaderResource(0, null);

        context.GenerateMips(_lumaSrv!);
    }

    private void DispatchTrack(DeviceContext context, Params parameters, Texture2D image, BufferWithViews seeds)
    {
        UploadParams(parameters);

        var stage = context.ComputeShader;
        stage.Set(_shaderResources!.Track.Value);
        stage.SetConstantBuffer(0, _paramsBuffer);
        stage.SetShaderResource(0, SrvManager.GetSrvForTexture(image));
        stage.SetShaderResource(1, _lumaSrv);
        stage.SetShaderResource(2, seeds.Srv);
        stage.SetUnorderedAccessView(0, _trackPoints.Uav);
        stage.SetUnorderedAccessView(1, _currentPoints.Uav);
        stage.SetUnorderedAccessView(2, _seedQuality.Uav);
        stage.SetUnorderedAccessView(3, _keyTemplates.Uav);
        stage.SetUnorderedAccessView(4, _recentTemplates.Uav);

        context.Dispatch(parameters.MarkerCount, 1, 1);

        for (var uavSlot = 0; uavSlot < 5; uavSlot++)
            stage.SetUnorderedAccessView(uavSlot, null);

        for (var srvSlot = 0; srvSlot < 3; srvSlot++)
            stage.SetShaderResource(srvSlot, null);
    }

    private void ClearTrack()
    {
        Array.Clear(_filledSlots);
        _trackedFrameCount = 0;
        _isSeeded = false;
        _recentTemplateSlot = -1;
        _needsFrameUpdate = true;

        var trackPointCount = _layout.MarkerCount * _layout.FrameCount;
        var clearParams = new Params { ClearCount = trackPointCount };
        UploadParams(clearParams);

        var context = ResourceManager.Device.ImmediateContext;
        var stage = context.ComputeShader;
        context.PushState(StateGroups.ComputeShader);
        stage.Set(_shaderResources!.Clear.Value);
        stage.SetConstantBuffer(0, _paramsBuffer);
        stage.SetUnorderedAccessView(0, _trackPoints.Uav);
        context.Dispatch((trackPointCount + 63) / 64, 1, 1);
        stage.SetUnorderedAccessView(0, null);
        context.PopState();
    }

    private bool SetupResources(TrackLayout layout)
    {
        try
        {
            var templateSize = layout.PatchSize + 2;
            var templateFloatCount = layout.MarkerCount * TemplateLevelCount * templateSize * templateSize;
            SetupBuffer(_trackPoints, layout.MarkerCount * layout.FrameCount, Point.Stride);
            SetupBuffer(_currentPoints, layout.MarkerCount, Point.Stride);
            SetupBuffer(_seedQuality, layout.MarkerCount, Point.Stride);
            SetupBuffer(_keyTemplates, templateFloatCount, sizeof(float));
            SetupBuffer(_recentTemplates, templateFloatCount, sizeof(float));

            if (layout.ImageSize != _layout.ImageSize)
                SetupLumaTexture(layout.ImageSize);

            if (_filledSlots.Length != layout.FrameCount)
                _filledSlots = new bool[layout.FrameCount];
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create tracking resources: {e.Message}", this);
            _layout = default;
            return false;
        }

        _layout = layout;
        return true;
    }

    private void SetupLumaTexture(Int2 size)
    {
        DisposeLumaTexture();
        var device = ResourceManager.Device;
        _lumaTexture = Texture2D.CreateTexture2D(new Texture2DDescription
                                                     {
                                                         ArraySize = 1,
                                                         BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess
                                                                                              | BindFlags.RenderTarget,
                                                         CpuAccessFlags = CpuAccessFlags.None,
                                                         Format = Format.R32_Float,
                                                         Width = size.Width,
                                                         Height = size.Height,
                                                         MipLevels = TemplateLevelCount,
                                                         OptionFlags = ResourceOptionFlags.GenerateMipMaps,
                                                         SampleDescription = new SampleDescription(1, 0),
                                                         Usage = ResourceUsage.Default,
                                                     });
        _lumaSrv = new ShaderResourceView(device, _lumaTexture);
        _lumaUav = new UnorderedAccessView(device, _lumaTexture);
    }

    private static void SetupBuffer(BufferWithViews buffer, int elementCount, int stride)
    {
        ResourceManager.SetupStructuredBuffer(elementCount * stride, stride, ref buffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(buffer.Buffer, ref buffer.Srv);
        ResourceManager.CreateStructuredBufferUav(buffer.Buffer, UnorderedAccessViewBufferFlags.None, ref buffer.Uav);
    }

    private void UploadParams(Params parameters)
    {
        if (_paramsBuffer == null)
        {
            ResourceManager.SetupConstBuffer(parameters, ref _paramsBuffer);
        }
        else
        {
            ResourceManager.UpdateConstBuffer(parameters, _paramsBuffer);
        }
    }

    /** Lucas-Kanade converges within about half a patch per level, and each level doubles the reach. */
    private static int LevelsForSearchRadius(float searchRadius, int patchSize)
    {
        var halfPatch = (patchSize - 1) / 2;
        for (var levels = 1; levels < TemplateLevelCount; levels++)
        {
            if (halfPatch * ((1 << levels) - 1) >= searchRadius)
                return levels;
        }

        return TemplateLevelCount;
    }

    private int FindNearestFilledSlot(int slot)
    {
        for (var distance = 1; distance < _filledSlots.Length; distance++)
        {
            if (IsFilled(slot - distance))
                return slot - distance;

            if (IsFilled(slot + distance))
                return slot + distance;
        }

        return -1;
    }

    private bool IsFilled(int slot) => slot >= 0 && slot < _filledSlots.Length && _filledSlots[slot];

    private void MarkFilled(int slot)
    {
        if (_filledSlots[slot])
            return;

        _filledSlots[slot] = true;
        _trackedFrameCount++;
    }

    private void DisposeLumaTexture()
    {
        GraphicsUtilities.Dispose(ref _lumaUav);
        GraphicsUtilities.Dispose(ref _lumaSrv);
        GraphicsUtilities.Dispose(ref _lumaTexture);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _trackPoints.Dispose();
        _currentPoints.Dispose();
        _seedQuality.Dispose();
        _keyTemplates.Dispose();
        _recentTemplates.Dispose();
        DisposeLumaTexture();
        GraphicsUtilities.Dispose(ref _paramsBuffer);
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
        => _statusMessage == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _statusMessage ?? string.Empty;

    private enum Modes
    {
        Capture = 0,
        Track = 1,
        Refresh = 2,
        Hide = 3,
    }

    private readonly record struct TrackLayout(int MarkerCount, int FrameCount, int FirstFrame, int SeedFrame,
                                               int PatchSize, Int2 ImageSize);

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct Params
    {
        [FieldOffset(0)] public Vector2 ImageSize;
        [FieldOffset(8)] public float MinConfidence;
        [FieldOffset(12)] public float QualityScale;
        [FieldOffset(16)] public int MarkerCount;
        [FieldOffset(20)] public int PatchSize;
        [FieldOffset(24)] public int Levels;
        [FieldOffset(28)] public int TemplateLevels;
        [FieldOffset(32)] public int Mode;
        [FieldOffset(36)] public int SourceSlot;
        [FieldOffset(40)] public int PreviousSlot;
        [FieldOffset(44)] public int TargetSlot;
        [FieldOffset(48)] public int UseRecentTemplate;
        [FieldOffset(52)] public int IterationsPerLevel;
        [FieldOffset(56)] public int ClearCount;
    }

    /** Loaded on first use with this instance as owner, so the package resolves the shader's includes. */
    private sealed class ShaderResources(TrackMarkers owner)
    {
        public readonly Resource<ComputeShader> Luma = ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, owner, () => "luma");
        public readonly Resource<ComputeShader> Clear = ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, owner, () => "clear");
        public readonly Resource<ComputeShader> Track = ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, owner, () => "track");
    }

    private const string ShaderPath = "Lib:shaders/points/track/TrackMarkers.hlsl";
    private const int MinPatchSize = 7;
    private const int MaxPatchSize = 31;
    private const int TemplateLevelCount = 5;
    private const int IterationsPerLevel = 4;
    private const int MaxFrameCount = 1_000_000;

    /** Bridges frames skipped by fast playback; larger jumps would cache a failed track as lost markers. */
    private const int MaxTrackingGapFrames = 8;

    /** 4M points of 64 bytes keep the track buffer at 256 MB. */
    private const long MaxTrackPointCount = 4_000_000;

    /** Gradient RMS in the weakest direction that counts as a perfect seed (luma 0–1, per pixel). */
    private const float GradientRmsForFullQuality = 0.05f;

    private ShaderResources? _shaderResources;
    private readonly BufferWithViews _trackPoints = new();
    private readonly BufferWithViews _currentPoints = new();
    private readonly BufferWithViews _seedQuality = new();
    private readonly BufferWithViews _keyTemplates = new();
    private readonly BufferWithViews _recentTemplates = new();
    private Texture2D? _lumaTexture;
    private ShaderResourceView? _lumaSrv;
    private UnorderedAccessView? _lumaUav;
    private Buffer? _paramsBuffer;
    private Params _lumaParams;

    private TrackLayout _layout;
    private bool[] _filledSlots = [];
    private int _trackedFrameCount;
    private bool _isSeeded;
    private int _recentTemplateSlot = -1;
    private int _lastFrameIndex = int.MinValue;
    private bool _needsFrameUpdate;
    private bool _resetTriggered;
    private string? _statusMessage;

    [Input(Guid = "d2a48b9a-5f1f-47ef-b979-e86570fedfc7")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "7638ba90-9f7b-414e-bc69-33d9b22e1658")]
    public readonly InputSlot<int> FrameIndex = new();

    [Input(Guid = "e619480e-b793-4dee-9168-cc4cb8cb6621")]
    public readonly InputSlot<BufferWithViews> SeedPoints = new();

    [Input(Guid = "6ed43e2d-e061-4e6b-aafd-090e800f49ee")]
    public readonly InputSlot<int> SeedFrame = new();

    [Input(Guid = "48bd87e4-8e48-4971-a0f5-78f59fe98254")]
    public readonly InputSlot<int> FirstFrame = new();

    [Input(Guid = "4e34e1a4-90dd-4581-8964-dc1c70627a64")]
    public readonly InputSlot<int> FrameCount = new();

    [Input(Guid = "6e618b85-7ffc-4d83-9410-5ebd4b01f0f2")]
    public readonly InputSlot<int> PatchSize = new();

    [Input(Guid = "47a01aa0-2b05-4c08-9683-8ffe1b6b3127")]
    public readonly InputSlot<float> SearchRadius = new();

    [Input(Guid = "b888df98-6cf6-4152-a8bc-9f12c43d5c9c")]
    public readonly InputSlot<float> MinConfidence = new();

    [Input(Guid = "2f715dd9-5cdb-41f4-81ce-f9b53de2639f")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "0b2c74e1-aa56-454e-95a0-6ac3eeea7e14")]
    public readonly InputSlot<bool> IsEnabled = new();
}
