using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Operator.Interfaces;
using T3.Core.Resource.Assets;
using T3.Core.Utils;
using T3.Core.Video;
using T3.VideoServices;

namespace Lib.io.video;

/// <summary>
/// Implemented by [VideoClip] so the [VideoClipPlayer] compositor ([_ProcessVideoClips]) can read each clip's
/// per-clip compositing params. The clip's TimeClip / LayerIndex come from the same output — the texture
/// output IS the time-clip slot.
/// </summary>
internal interface IVideoClipProvider
{
    Slot<Texture2D> TextureOutput { get; }
    InputSlot<Vector4> ColorInput { get; }
    InputSlot<int> BlendModeInput { get; }

    /// <summary>Called by a [VideoClipPlayer] each frame for every clip it manages, so an unmanaged clip can hint.</summary>
    void MarkManaged();
}

[Guid("04c1a6dc-3042-48a8-81d2-0a5a162016dc")]
internal sealed class VideoClip : Instance<VideoClip>, IStatusProvider, IVideoClipProvider, IContentTimeClip, IDescriptiveFilename, IAudioSource
{
    // The texture output is both the frame and the timeline placement: a TimeClipSlot carries the
    // TimeClip data, with the out-of-range gate disabled so the player can pre-roll the decoder and
    // out-of-range pulls resolve to the clamped first/last frame.
    [Output(Guid = "eb954aeb-535b-4b22-ac49-858f71bdaac4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly TimeClipSlot<Texture2D> Texture = new();

    /// <summary>
    /// AudioReference for the audio-processing graph. Wire it into an [AudioBus] (directly or through a
    /// [CombineAudio]) to route this clip's sound through the graph, so group volume, FX inserts and ducking
    /// apply to it. Left unwired it plays through the implicit default bus.
    /// </summary>
    [Output(Guid = "d9da88fb-a5ac-451f-8727-a8ce126432d8", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<AudioGraphNode> AudioReference = new();

    public VideoClip()
    {
        Texture.EvaluateOutsideRange = true;
        Texture.UpdateAction += Update;

        // The decode side owns the stream's lifetime and feed position; the graph only decides which mixer it
        // joins and at what gain.
        _node = new AudioGraphNode(this) { ExternallyManagedChannel = true };
        AudioReference.Value = _node;
        AudioReference.UpdateAction += UpdateAudioReference;
    }

    private void Update(EvaluationContext context)
    {
        var relativePath = Path.GetValue(context);
        if (!AssetRegistry.TryResolveAddress(relativePath, this, out var absolutePath, out _))
        {
            _statusMessage = "Can't find video " + relativePath;
            Texture.DirtyFlag.Clear();
            return;
        }

        // LocalTime already arrives remapped into source time (bars) — the clip slot remaps without
        // gating (EvaluateOutsideRange), extrapolated outside the clip so pre-roll pulls still work.
        var sourceRange = Texture.TimeClip.SourceRange;
        var sourceTimeInSecs = context.Playback.SecondsFromBars(context.LocalTime);

        // Clamp to the clip's source range so times outside the clip resolve to its first/last frame; the
        // controller additionally clamps to the video's real duration.
        var sourceStart = context.Playback.SecondsFromBars(sourceRange.Start);
        var sourceEnd = context.Playback.SecondsFromBars(sourceRange.End);
        var clampedTime = Math.Clamp(sourceTimeInSecs, Math.Min(sourceStart, sourceEnd), Math.Max(sourceStart, sourceEnd));

        var result = VideoPlaybackEngine.Instance.RequestFrame(_streamId, absolutePath, clampedTime,
                                                               loop: false, context.Playback.IsRenderingToFile,
                                                               (VideoPlaybackOptimization)OptimizeFor.GetValue(context));
        _statusMessage = result.ErrorMessage;
        Texture.Value = result.Texture;

        // "Active" means inside SourceRange in source time (min/max so a reversed clip still gates). The player
        // also pulls clips ahead of their cut to pre-warm the decoder; those pulls must neither be heard nor
        // stall export waiting for a frame that isn't on screen yet.
        var isActive = context.LocalTime >= Math.Min(sourceRange.Start, sourceRange.End)
                       && context.LocalTime < Math.Max(sourceRange.Start, sourceRange.End);

        UpdateAudioTrack(context, absolutePath, clampedTime, isActive);

        if (context.Playback.IsRenderingToFile && isActive)
            Playback.OpNotReady |= !result.IsReady;

        Texture.DirtyFlag.Clear();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        VideoPlaybackEngine.Instance.ReleaseStream(_streamId);
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        if (!string.IsNullOrEmpty(_statusMessage))
            return IStatusProvider.StatusLevel.Error;

        return IsManagedByAPlayer ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Notice;
    }

    public string GetStatusMessage()
    {
        if (!string.IsNullOrEmpty(_statusMessage))
            return _statusMessage;

        return IsManagedByAPlayer ? null : "Not drawn by any [VideoClipPlayer] — wire it into one or enable the player's AutoCollect.";
    }

    Slot<AudioGraphNode> IAudioSource.AudioReferenceOutput => AudioReference;

    // Nothing to do: the channel is created by the evaluated texture path, never from static inputs. A clip
    // no player draws has no picture, and so should have no sound either.
    void IAudioSource.EnsureChannelFromStaticInputs()
    {
    }

    Slot<Texture2D> IVideoClipProvider.TextureOutput => Texture;
    InputSlot<Vector4> IVideoClipProvider.ColorInput => Color;
    InputSlot<int> IVideoClipProvider.BlendModeInput => BlendMode;

    // Drives the timeline clip label (filename, or "RenamedName (file)") instead of the op's symbol name.
    InputSlot<string> IDescriptiveFilename.SourcePathSlot => Path;

    // A [VideoClipPlayer] stamps the current frame on every clip it manages — wired or auto-collected, and even
    // while the clip sits between its in/out points. A clip no player has stamped for a couple of frames is
    // drawn by nobody, so its status hints at that instead of silently showing nothing.
    void IVideoClipProvider.MarkManaged() => _lastManagedFrame = Playback.FrameCount;
    private bool IsManagedByAPlayer => Playback.FrameCount - _lastManagedFrame <= ManagedFrameSlack;
    private const int ManagedFrameSlack = 2;

    // Sound follows the picture: the texture path owns the source time, so it also drives the audio track.
    // Not requesting for a few frames mutes the track, which is what keeps pre-roll and un-drawn clips silent.
    private void UpdateAudioTrack(EvaluationContext context, string absolutePath, double sourceTime, bool isActive)
    {
        _lastAudioRequestFrame = Playback.FrameCount;

        var volume = Volume.GetValue(context);
        _node.Gain = volume;

        // A silenced source should cost no decoding at all, so a video used purely as a texture stays free.
        // While rendering, the engine feeds deterministically instead of against the wall clock.
        _node.SourceChannel = isActive && volume > 0
                                  ? VideoPlaybackEngine.Instance.RequestAudio(_streamId, absolutePath, sourceTime,
                                                                              loop: false, context.Playback.IsRenderingToFile)
                                  : 0;
    }

    // Off the render path: a bus evaluating this output supplies no meaningful source time, so the node only
    // republishes the channel and gain the texture path already established.
    private void UpdateAudioReference(EvaluationContext context)
    {
        // A clip no player draws stops running the texture path, and the engine eventually evicts its stream and
        // frees the channel. Keep advertising it and a routing bus retries a dead handle every frame, forever.
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
    private string _statusMessage;
    private int _lastManagedFrame;

    // Input parameters
    [Input(Guid = "31721e18-556b-452b-a8aa-18dbd44af74d")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "28f27625-37fe-409a-b6c1-d4eabf6c1eb8")]
    public readonly InputSlot<float> Volume = new();

    // No longer used: FFmpeg gives direct PTS control, so there is no resync threshold.
    [Input(Guid = "5EB10090-AE6A-4AE7-9FBD-5BD9FFD13B1B")]
    public readonly InputSlot<float> ResyncThreshold = new();

    // Per-clip compositing params, read by [VideoClipPlayer] when stacking active clips. Color is tint + alpha
    // (alpha = opacity); it rides context.ForegroundColor into the composite.
    [Input(Guid = "7fb1d490-6c9b-4380-b88c-800d44c16475")]
    public readonly InputSlot<Vector4> Color = new(Vector4.One);

    [Input(Guid = "27f02af3-06f3-4cb2-8731-2e8777634275", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();

    // Fast Seeking (default) decodes in software with a RAM cache for snappy scrub-back and smooth HD playback;
    // Playback Performance decodes zero-copy on the GPU for the smoothest large/4K playback.
    [Input(Guid = "f7e8f5f1-3333-409f-92da-573b1f32c0d6", MappedType = typeof(VideoPlaybackOptimization))]
    public readonly InputSlot<int> OptimizeFor = new();
}
