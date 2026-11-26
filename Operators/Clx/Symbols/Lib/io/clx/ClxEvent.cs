using Clockworks.CLX;
using Clx.Utils;
using T3.Core.Animation;

namespace Lib.io.clx;

[Guid("2d72dc80-3173-4c48-a790-82e98bd3ce7f")]
internal sealed class ClxEvent : Instance<ClxEvent>, IStatusProvider
{
    [Output(Guid = "afc0616f-384c-4b4f-9b8f-cffd43801724", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasTriggered = new();

    [Output(Guid = "5535ef66-3221-477b-9ef5-5a6362f770bf", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> LastEventName = new();

    [Output(Guid = "19571a23-656e-4e9a-b2f5-19c38627bd6e", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> LastDeck = new();

    [Output(Guid = "4d9c9670-9364-4527-8484-e9b937ebe19d", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Values = new();

    [Output(Guid = "3dd2074f-b4ad-4880-b7b4-77bc6d5de1c6", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Count = new();

    public ClxEvent()
    {
        WasTriggered.UpdateAction += Update;
        LastEventName.UpdateAction += Update;
        LastDeck.UpdateAction += Update;
        Values.UpdateAction += Update;
        Count.UpdateAction += Update;
        Values.Value = _values;
    }

    private void Update(EvaluationContext context)
    {
        if (Playback.FrameCount == _lastFrame)
            return;

        _lastFrame = Playback.FrameCount;
        WasTriggered.Value = false;

        var client = _binding.Update(Port.GetValue(context), Listen.GetValue(context));
        if (client == null)
            return;

        var nameFilter = EventName.GetValue(context);
        var deckFilter = Deck.GetValue(context);
        var events = _binding.Connection.RecentEvents;

        // On the first update, or after the port changed, start from what arrives next instead
        // of firing for events that came in before.
        if (_binding.Connection != _seenConnection)
        {
            _seenConnection = _binding.Connection;
            _lastSequence = events.Count > 0 ? events[^1].Sequence : 0;
            return;
        }

        foreach (var received in events)
        {
            if (received.Sequence <= _lastSequence)
                continue;

            _lastSequence = received.Sequence;
            var packet = received.Packet;
            if (!string.IsNullOrEmpty(nameFilter) && !string.Equals(packet.EventName, nameFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (deckFilter > 0 && packet.Deck != deckFilter)
                continue;

            WasTriggered.Value = true;
            _count++;
            LastEventName.Value = packet.EventName ?? string.Empty;
            LastDeck.Value = packet.Deck;
            _values.Clear();
            var valueCount = packet.Values?.Length ?? 0;
            for (var i = 0; i < valueCount; i++)
                _values.Add((float)packet.GetDouble(i, double.NaN));
        }

        Values.Value = _values;
        Count.Value = _count;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            _binding.Release();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _binding.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _binding.GetStatusMessage();

    private readonly ClxBinding _binding = new();
    private readonly List<float> _values = new();
    private ClxConnections.Connection _seenConnection;
    private long _lastSequence;
    private int _count;
    private int _lastFrame = -1;

    [Input(Guid = "e3c30ec6-802e-4adf-93ad-084e71178530")]
    public readonly InputSlot<string> EventName = new();

    [Input(Guid = "17a6685b-f3e3-4d85-8af8-9f919b91689f")]
    public readonly InputSlot<int> Deck = new();

    [Input(Guid = "a0538523-1ded-4c7d-a7b1-174289cf1d35")]
    public readonly InputSlot<int> Port = new();

    [Input(Guid = "8132a4b9-9d3c-48fe-8620-04441b845e49")]
    public readonly InputSlot<bool> Listen = new();
}
