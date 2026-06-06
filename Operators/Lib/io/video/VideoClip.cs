using T3.Core.Resource.Assets;
using T3.Video;

namespace Lib.io.video;

[Guid("04c1a6dc-3042-48a8-81d2-0a5a162016dc")]
internal sealed class VideoClip :Instance<VideoClip>,IStatusProvider
{
    [Output(Guid = "eb954aeb-535b-4b22-ac49-858f71bdaac4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Texture = new();

    [Output(Guid = "30357595-0893-47F8-8BCA-22DD77275768", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly TimeClipSlot<Command> TimeSlot = new();

    public VideoClip()
    {
        Texture.UpdateAction += Update;
        TimeSlot.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Command.GetValue(context);

        var relativePath = Path.GetValue(context);
        if (!AssetRegistry.TryResolveAddress(relativePath, this, out var absolutePath, out _))
        {
            _statusMessage = "Can't find video " + relativePath;
            Texture.DirtyFlag.Clear();
            return;
        }

        // Map the timeline position into the clip's source time, applying the per-clip playback rate.
        var timeRange = TimeSlot.TimeClip.TimeRange;
        var sourceRange = TimeSlot.TimeClip.SourceRange;

        var barsInSeconds = context.LocalTime - timeRange.Start;
        if (timeRange.End != timeRange.Start)
        {
            var rate = (sourceRange.End - sourceRange.Start) / (timeRange.End - timeRange.Start);
            barsInSeconds *= rate;
        }

        barsInSeconds += sourceRange.Start;
        var sourceTimeInSecs = context.Playback.SecondsFromBars(barsInSeconds);

        // Clamp to the clip's source range so times outside the clip resolve to its first/last frame; the
        // controller additionally clamps to the video's real duration.
        var sourceStart = context.Playback.SecondsFromBars(sourceRange.Start);
        var sourceEnd = context.Playback.SecondsFromBars(sourceRange.End);
        var clampedTime = Math.Clamp(sourceTimeInSecs, Math.Min(sourceStart, sourceEnd), Math.Max(sourceStart, sourceEnd));

        var result = VideoPlaybackEngine.Instance.RequestFrame(_streamId, absolutePath, clampedTime,
                                                               loop: false, context.Playback.IsRenderingToFile);
        _statusMessage = result.ErrorMessage;
        Texture.Value = result.Texture;

        Texture.DirtyFlag.Clear();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        VideoPlaybackEngine.Instance.ReleaseStream(_streamId);
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
        => string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;

    public string GetStatusMessage() => _statusMessage;

    private readonly Guid _streamId = Guid.NewGuid();
    private string _statusMessage;

    // Input parameters
    [Input(Guid = "10c311ee-6426-463a-a1fe-cfac6de04224")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "31721e18-556b-452b-a8aa-18dbd44af74d")]
    public readonly InputSlot<string> Path = new();

    // Audio is silent in this milestone (BASS routing is backlog); kept for graph compatibility.
    [Input(Guid = "28f27625-37fe-409a-b6c1-d4eabf6c1eb8")]
    public readonly InputSlot<float> Volume = new();

    // No longer used: FFmpeg gives direct PTS control, so there is no resync threshold.
    [Input(Guid = "5EB10090-AE6A-4AE7-9FBD-5BD9FFD13B1B")]
    public readonly InputSlot<float> ResyncThreshold = new();
}
