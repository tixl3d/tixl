using Clockworks.CLX;
using Clx.Utils;
using T3.Core.Animation;

namespace Lib.io.clx;

[Guid("66ccde72-d3ff-462f-bf72-45c6b59d9783")]
internal sealed class ClxBeatgrid : Instance<ClxBeatgrid>, IStatusProvider
{
    [Output(Guid = "16cf29fb-f3a4-4473-b43d-d1511854df49", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> BeatTimes = new();

    [Output(Guid = "f2282bf1-f15f-4c72-813a-4c0971eb9589", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> DownbeatTimes = new();

    [Output(Guid = "15ba376c-2405-4d1a-af3c-7214c2785d22", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> MarkerTimes = new();

    [Output(Guid = "f2c30a95-9312-440b-82ba-ea46a33b35d1", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> MarkerBpms = new();

    [Output(Guid = "cc04da54-1247-467c-b778-755f975035a0", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> BeatCount = new();

    public ClxBeatgrid()
    {
        BeatTimes.UpdateAction += Update;
        DownbeatTimes.UpdateAction += Update;
        MarkerTimes.UpdateAction += Update;
        MarkerBpms.UpdateAction += Update;
        BeatCount.UpdateAction += Update;
        BeatTimes.Value = _beatTimes;
        DownbeatTimes.Value = _downbeatTimes;
        MarkerTimes.Value = _markerTimes;
        MarkerBpms.Value = _markerBpms;
    }

    private void Update(EvaluationContext context)
    {
        if (Playback.FrameCount == _lastFrame)
            return;

        _lastFrame = Playback.FrameCount;

        var deck = Deck.GetValue(context);
        var client = _binding.Update(Port.GetValue(context), Listen.GetValue(context));
        var grid = client?.GetBeatgrid(deck);
        var length = ClxTrackLength.Get(client, deck);

        if (grid == _lastGrid && Math.Abs(length - _lastLength) < 0.001)
            return;

        _lastGrid = grid;
        _lastLength = length;

        _beatTimes.Clear();
        _downbeatTimes.Clear();
        _markerTimes.Clear();
        _markerBpms.Clear();
        if (grid != null)
        {
            // Without a known length the last marker's tempo can't be continued, so only the
            // markers themselves are expanded.
            foreach (var beat in grid.ExpandBeats(length > 0 ? (float)length : 0))
            {
                _beatTimes.Add(beat.Time);
                if (beat.Index % 4 == 0)
                    _downbeatTimes.Add(beat.Time);
            }

            foreach (var marker in grid.Markers)
            {
                _markerTimes.Add(marker.Position);
                _markerBpms.Add(marker.Bpm);
            }
        }

        BeatTimes.Value = _beatTimes;
        DownbeatTimes.Value = _downbeatTimes;
        MarkerTimes.Value = _markerTimes;
        MarkerBpms.Value = _markerBpms;
        BeatCount.Value = _beatTimes.Count;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            _binding.Release();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _binding.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _binding.GetStatusMessage();

    private readonly ClxBinding _binding = new();
    private readonly List<float> _beatTimes = new();
    private readonly List<float> _downbeatTimes = new();
    private readonly List<float> _markerTimes = new();
    private readonly List<float> _markerBpms = new();
    private BeatgridPayload _lastGrid;
    private double _lastLength = -1;
    private int _lastFrame = -1;

    [Input(Guid = "3e1b89a4-ea21-4974-aa9a-b44c50974a9d")]
    public readonly InputSlot<int> Deck = new();

    [Input(Guid = "e8f33b7e-5e29-44eb-a078-738c2ddbe0fb")]
    public readonly InputSlot<int> Port = new();

    [Input(Guid = "a4fffd80-4a5b-4ca4-b96c-df5eff655add")]
    public readonly InputSlot<bool> Listen = new();
}
