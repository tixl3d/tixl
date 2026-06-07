#nullable enable
using T3.Core.Animation;
using T3.Core.Resource.Assets;
using T3.Core.Video;
using T3.Video;

namespace Lib.io.video;

[Guid("914fb032-d7eb-414b-9e09-2bdd7049e049")]
internal sealed class PlayVideo : Instance<PlayVideo>, IStatusProvider
{
    [Output(Guid = "fa56b47f-1b16-45d5-80cd-32c5a872acf4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> Texture = new();

    [Output(Guid = "2F16BE73-226B-47E7-B7EE-BF4F3738FA13")]
    public readonly Slot<float> Duration = new();

    [Output(Guid = "C89EA3AE-82FF-4791-B755-7B7D9EDDF8A7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasCompleted = new();

    [Output(Guid = "732FC715-A8B5-438F-A607-EE1B8B080C04", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    public PlayVideo()
    {
        Texture.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var requestedTime = OverrideTimeInSecs.HasInputConnections
                                ? OverrideTimeInSecs.GetValue(context)
                                : context.Playback.SecondsFromBars(context.LocalTime);

        var relativePath = Path.GetValue(context);
        if (!AssetRegistry.TryResolveAddress(relativePath, this, out var absolutePath, out _))
        {
            _statusMessage = "Can't find video " + relativePath;
            return;
        }

        var result = VideoPlaybackEngine.Instance.RequestFrame(_streamId, absolutePath!, requestedTime,
                                                               Loop.GetValue(context), context.Playback.IsRenderingToFile,
                                                               (VideoPlaybackOptimization)OptimizeFor.GetValue(context));
        if (result.Produced)
            UpdateCount.Value++;

        _statusMessage = result.ErrorMessage;
        HasCompleted.Value = result.HasCompleted;
        Texture.Value = result.Texture;
        Duration.Value = result.Duration;

        // Only stall the exporter for the exact frame; realtime keeps showing the last valid texture.
        if (context.Playback.IsRenderingToFile)
            Playback.OpNotReady |= !result.IsReady;

        Texture.DirtyFlag.Clear();
        Duration.DirtyFlag.Clear();
        HasCompleted.DirtyFlag.Clear();
        UpdateCount.DirtyFlag.Clear();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        VideoPlaybackEngine.Instance.ReleaseStream(_streamId);
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
        => string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;

    public string? GetStatusMessage() => _statusMessage;

    private readonly Guid _streamId = Guid.NewGuid();
    private string? _statusMessage;

    // Input parameters
    [Input(Guid = "0e255347-08bc-4363-9ffa-ab863a1cea8e")]
    public readonly InputSlot<string> Path = new();

    // Audio is silent in this milestone (routing through the BASS engine is backlog); kept for compatibility.
    [Input(Guid = "2FECFBB4-F7D9-4C53-95AE-B64CCBB6FBAD")]
    public readonly InputSlot<float> Volume = new();

    // No longer used: FFmpeg gives direct PTS control, so there is no resync threshold or start offset.
    [Input(Guid = "E9C15B3F-8C4A-411D-B9B3-795D64D6BD20")]
    public readonly InputSlot<float> ResyncThreshold = new();

    [Input(Guid = "48E62A3C-A903-4A9B-A44A-148C6C07AC1E")]
    public readonly InputSlot<float> OverrideTimeInSecs = new();

    [Input(Guid = "21B5671B-862F-4CEA-A355-FA019996C936")]
    public readonly InputSlot<bool> Loop = new();

    // No longer used: FFmpeg's frame-exact PTS control makes paused→play seamless without the old precise-mode
    // start-offset priming. Kept (slot + GUID) for graph compatibility with existing projects.
    [Input(Guid = "B62C208C-3735-4130-87DE-8C03C8A9B5FA")]
    public readonly InputSlot<bool> IsPreciseAtPlayback = new();

    // Fast Seeking (default) decodes in software with a RAM cache for snappy scrub-back and smooth HD playback;
    // Playback Performance decodes zero-copy on the GPU for the smoothest large/4K playback.
    [Input(Guid = "28c8b698-1897-4f8c-b9e7-85a983dfa654", MappedType = typeof(VideoPlaybackOptimization))]
    public readonly InputSlot<int> OptimizeFor = new();
}
