using Clockworks.CLX;
using Clx.Utils;
using T3.Core.Animation;

namespace Lib.io.clx;

[Guid("f821b479-6db6-4bbf-8f95-82649b0680b7")]
internal sealed class ClxLeader : Instance<ClxLeader>, IStatusProvider
{
    [Output(Guid = "506fcc41-965e-4fdc-82b4-6928e0be705d", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Position = new();

    [Output(Guid = "5f57b612-988d-4678-b18b-c2ed9aa93e70", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> PositionNow = new();

    [Output(Guid = "3cda46cc-04ec-4b3f-9f0b-08a45fa85e66", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Delay = new();

    [Output(Guid = "aec3384c-a399-432e-925b-b88e2e4b0ae5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> LiveDeck = new();

    [Output(Guid = "e477f574-e4af-49b2-8a33-6eefeaa9544f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> Project = new();

    [Output(Guid = "04cb902e-235f-4108-a23a-fc3051e55873", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasData = new();

    public ClxLeader()
    {
        Position.UpdateAction += Update;
        PositionNow.UpdateAction += Update;
        Delay.UpdateAction += Update;
        LiveDeck.UpdateAction += Update;
        Project.UpdateAction += Update;
        HasData.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Playback.FrameCount == _lastFrame)
            return;

        _lastFrame = Playback.FrameCount;

        var client = _binding.Update(Port.GetValue(context), Listen.GetValue(context));
        var hasData = client != null && client.TryGetLeader(out _packet);
        HasData.Value = hasData;
        if (!hasData)
            return;

        // Tx is the sender's clock at send time. With a shared clock (PTP, NTP) now - Tx is the
        // one-way delay; anything negative or over a second means the clocks aren't synchronised.
        var delay = -1.0;
        if (_packet.HasTx)
        {
            var nowNs = (DateTime.UtcNow - DateTime.UnixEpoch).Ticks * 100L;
            var seconds = (nowNs - (long)_packet.Tx) / 1e9;
            if (seconds is >= 0 and <= 1)
                delay = seconds;
        }

        Position.Value = (float)_packet.Position;
        PositionNow.Value = (float)(_packet.Position + Math.Max(0, delay));
        Delay.Value = (float)delay;
        LiveDeck.Value = _packet.LiveDeck;
        Project.Value = _packet.Project ?? string.Empty;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            _binding.Release();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _binding.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _binding.GetStatusMessage();

    private readonly ClxBinding _binding = new();
    private LeaderPacket _packet;
    private int _lastFrame = -1;

    [Input(Guid = "0adcb8f4-6dba-48b5-a3d6-cc86c889ea00")]
    public readonly InputSlot<int> Port = new();

    [Input(Guid = "2ecf216f-5507-4286-af6c-efb891267e21")]
    public readonly InputSlot<bool> Listen = new();
}
