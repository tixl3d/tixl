using Clockworks.CLX;
using Clx.Utils;
using T3.Core.Animation;

namespace Lib.io.clx;

[Guid("96de6e3f-3c39-405e-8928-bffb4c2880fd")]
internal sealed class ClxCues : Instance<ClxCues>, IStatusProvider
{
    [Output(Guid = "eb9b81fa-7529-4b56-8385-cd47ca7b7717", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Times = new();

    [Output(Guid = "eeb12d11-fece-45e0-9c3e-2f8529421ea5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> NormalizedTimes = new();

    [Output(Guid = "2e059940-a279-4b19-bed1-4048c4cf76b4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<string>> Names = new();

    [Output(Guid = "e77334cc-caf9-4419-bb56-b143f5211ccf", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> IsHotcue = new();

    [Output(Guid = "6968c325-7a0c-4130-b32a-541ed37542af", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Count = new();

    public ClxCues()
    {
        Times.UpdateAction += Update;
        NormalizedTimes.UpdateAction += Update;
        Names.UpdateAction += Update;
        IsHotcue.UpdateAction += Update;
        Count.UpdateAction += Update;
        Times.Value = _times;
        NormalizedTimes.Value = _normalizedTimes;
        Names.Value = _names;
        IsHotcue.Value = _isHotcue;
    }

    private void Update(EvaluationContext context)
    {
        if (Playback.FrameCount == _lastFrame)
            return;

        _lastFrame = Playback.FrameCount;

        var deck = Deck.GetValue(context);
        var hotcuesOnly = HotcuesOnly.GetValue(context);
        var client = _binding.Update(Port.GetValue(context), Listen.GetValue(context));
        var cues = client?.GetCues(deck);
        var length = ClxTrackLength.Get(client, deck);

        if (cues == _lastCues && hotcuesOnly == _lastHotcuesOnly && Math.Abs(length - _lastLength) < 0.001)
            return;

        _lastCues = cues;
        _lastHotcuesOnly = hotcuesOnly;
        _lastLength = length;

        _times.Clear();
        _normalizedTimes.Clear();
        _names.Clear();
        _isHotcue.Clear();
        if (cues != null)
        {
            foreach (var cue in cues.Cues.OrderBy(c => c.Time))
            {
                if (hotcuesOnly && !cue.Hotcue)
                    continue;

                _times.Add(cue.Time);
                _normalizedTimes.Add(length > 0 ? (float)(cue.Time / length) : 0);
                _names.Add(cue.Name ?? string.Empty);
                _isHotcue.Add(cue.Hotcue ? 1 : 0);
            }
        }

        Times.Value = _times;
        NormalizedTimes.Value = _normalizedTimes;
        Names.Value = _names;
        IsHotcue.Value = _isHotcue;
        Count.Value = _times.Count;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            _binding.Release();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _binding.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _binding.GetStatusMessage();

    private readonly ClxBinding _binding = new();
    private readonly List<float> _times = new();
    private readonly List<float> _normalizedTimes = new();
    private readonly List<string> _names = new();
    private readonly List<float> _isHotcue = new();
    private CuesPayload _lastCues;
    private bool _lastHotcuesOnly;
    private double _lastLength = -1;
    private int _lastFrame = -1;

    [Input(Guid = "ccddf67c-1174-4851-b069-0e347c1a17a0")]
    public readonly InputSlot<int> Deck = new();

    [Input(Guid = "22077b6f-8374-434b-b514-5ab3017ad42f")]
    public readonly InputSlot<bool> HotcuesOnly = new();

    [Input(Guid = "f61cf529-3248-4fb9-971d-103068c8e023")]
    public readonly InputSlot<int> Port = new();

    [Input(Guid = "9c4925a3-3e23-4f86-a73d-7987d76d90e7")]
    public readonly InputSlot<bool> Listen = new();
}
