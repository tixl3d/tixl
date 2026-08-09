#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Resource.Assets;
using T3.Core.Video;
using T3.VideoServices;

namespace Lib.io.video;

[Guid("914fb032-d7eb-414b-9e09-2bdd7049e049")]
internal sealed class PlayVideo : Instance<PlayVideo>, IStatusProvider, IAudioSource
{
    [Output(Guid = "fa56b47f-1b16-45d5-80cd-32c5a872acf4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> Texture = new();

    [Output(Guid = "2F16BE73-226B-47E7-B7EE-BF4F3738FA13")]
    public readonly Slot<float> Duration = new();

    [Output(Guid = "C89EA3AE-82FF-4791-B755-7B7D9EDDF8A7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasCompleted = new();

    [Output(Guid = "732FC715-A8B5-438F-A607-EE1B8B080C04", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    /// <summary>
    /// AudioReference for the audio-processing graph. Wire it into an [AudioBus] (directly or through a
    /// [CombineAudio]) to route this video's sound through the graph, so group volume, FX inserts and ducking
    /// apply to it. Left unwired it plays through the implicit default bus.
    /// </summary>
    [Output(Guid = "12473a41-5839-4b9b-9c79-2541fe8b630b", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<AudioGraphNode> AudioReference = new();

    public PlayVideo()
    {
        Texture.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;

        // The decode side owns the stream's lifetime and feed position; the graph only decides which mixer it
        // joins and at what gain.
        _node = new AudioGraphNode(this) { ExternallyManagedChannel = true };
        AudioReference.Value = _node;
        AudioReference.UpdateAction = UpdateAudioReference;
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

        var loop = Loop.GetValue(context);
        var result = VideoPlaybackEngine.Instance.RequestFrame(_streamId, absolutePath!, requestedTime,
                                                               loop, context.Playback.IsRenderingToFile,
                                                               (VideoPlaybackOptimization)OptimizeFor.GetValue(context));
        if (result.Produced)
            UpdateCount.Value++;

        UpdateAudioTrack(context, absolutePath!, requestedTime, loop);

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

    Slot<AudioGraphNode> IAudioSource.AudioReferenceOutput => AudioReference;

    // Nothing to do: the channel is created by the evaluated texture path, never from static inputs. A video
    // that is not evaluated has no picture, and so should have no sound either.
    void IAudioSource.EnsureChannelFromStaticInputs()
    {
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

    // Sound follows the picture: the texture path owns the playback time, so it also drives the audio track.
    // Not requesting for a few frames mutes the track, which is what makes an un-evaluated video silent.
    private void UpdateAudioTrack(EvaluationContext context, string absolutePath, double requestedTime, bool loop)
    {
        _lastAudioRequestFrame = Playback.FrameCount;

        var volume = Volume.GetValue(context);
        _node.Gain = volume;

        // A silenced source should cost no decoding at all, so a video used purely as a texture stays free.
        // While rendering, the engine feeds deterministically instead of against the wall clock.
        _node.SourceChannel = volume > 0
                                  ? VideoPlaybackEngine.Instance.RequestAudio(_streamId, absolutePath, requestedTime,
                                                                              loop, context.Playback.IsRenderingToFile)
                                  : 0;
    }

    // Off the render path: a bus evaluating this output supplies no meaningful playback time, so the node only
    // republishes the channel and gain the texture path already established.
    private void UpdateAudioReference(EvaluationContext context)
    {
        // Once the texture path stops running, the engine eventually evicts the stream and frees the channel.
        // Keep advertising it and a routing bus retries a dead handle every frame, forever.
        if (Playback.FrameCount - _lastAudioRequestFrame > AudioRequestFrameSlack)
            _node.SourceChannel = 0;

        _node.Update(context);
        AudioReference.DirtyFlag.Clear();
    }

    // The bus may evaluate this output before the texture path runs in the same frame, so allow a frame of lead.
    private const int AudioRequestFrameSlack = 2;

    private readonly Guid _streamId = Guid.NewGuid();
    private readonly AudioGraphNode _node;
    private int _lastAudioRequestFrame = int.MinValue / 2;
    private string? _statusMessage;

    // Input parameters
    [Input(Guid = "0e255347-08bc-4363-9ffa-ab863a1cea8e")]
    public readonly InputSlot<string> Path = new();

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
