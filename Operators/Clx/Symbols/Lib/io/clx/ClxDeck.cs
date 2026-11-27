using Clockworks.CLX;
using Clx.Utils;
using T3.Core.Animation;

namespace Lib.io.clx;

[Guid("849896ae-78b9-4ab8-891c-0a90e651e004")]
internal sealed class ClxDeck : Instance<ClxDeck>, IStatusProvider
{
    [Output(Guid = "e6f4f870-f2f3-4584-b7b2-25eece3e1319", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Position = new();

    [Output(Guid = "f16e8edb-6b8f-483e-a8a1-7374e98cbfac", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> NormalizedPosition = new();

    [Output(Guid = "db1255c6-bc57-48ac-9614-4fbaf49fcb6a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Beats = new();

    [Output(Guid = "846b5fc4-4b34-43bd-ba97-cb2b2edfae52", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Bars = new();

    [Output(Guid = "a28d5b55-a8e0-4d42-93e3-d961252c7e9c", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> BeatPhase = new();

    [Output(Guid = "d5828520-a7b8-49e5-b8ad-93e64edccee3", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> BeatInBar = new();

    [Output(Guid = "a42a6140-7771-4afe-bd9b-009b09052430", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Bpm = new();

    [Output(Guid = "741d0974-c881-4510-955c-64afe7231068", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Pitch = new();

    [Output(Guid = "4fbed8d7-a08e-4f3e-9c7a-dd7211be0ede", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Length = new();

    [Output(Guid = "3371b439-cae6-4a62-9eb4-e00b0951375a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Eq = new();

    [Output(Guid = "cbe3c4bc-e1b9-4933-b528-298aa7dc610e", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> State = new();

    [Output(Guid = "d407b3d7-41f7-4da3-96d8-ea7e1ca298c1", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasData = new();

    public ClxDeck()
    {
        Position.UpdateAction += Update;
        NormalizedPosition.UpdateAction += Update;
        Beats.UpdateAction += Update;
        Bars.UpdateAction += Update;
        BeatPhase.UpdateAction += Update;
        BeatInBar.UpdateAction += Update;
        Bpm.UpdateAction += Update;
        Pitch.UpdateAction += Update;
        Length.UpdateAction += Update;
        Eq.UpdateAction += Update;
        State.UpdateAction += Update;
        HasData.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Playback.FrameCount == _lastFrame)
            return;

        _lastFrame = Playback.FrameCount;

        var client = _binding.Update(Port.GetValue(context), Listen.GetValue(context));
        var hasData = client != null && client.TryGetDeck(Deck.GetValue(context), out _packet);
        HasData.Value = hasData;
        if (!hasData)
            return;

        var beats = _packet.Position2;
        Position.Value = (float)_packet.Position;
        NormalizedPosition.Value = (float)_packet.NormalizedPosition;
        Beats.Value = (float)beats;
        Bars.Value = (float)(beats / 4.0);
        BeatPhase.Value = (float)(beats - Math.Floor(beats));
        BeatInBar.Value = _packet.Beat is >= 1 and <= 4 ? _packet.Beat : 0;
        Bpm.Value = (float)_packet.BPM;
        Pitch.Value = (float)_packet.Pitch;
        Length.Value = (float)_packet.Length;
        Eq.Value = new Vector3(_packet.EQLow, _packet.EQMid, _packet.EQHigh);
        State.Value = _packet.State;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            _binding.Release();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _binding.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _binding.GetStatusMessage();

    private readonly ClxBinding _binding = new();
    private DeckPacket _packet;
    private int _lastFrame = -1;

    [Input(Guid = "302f3e68-9016-4892-b70c-cea89082e73f")]
    public readonly InputSlot<int> Deck = new();

    [Input(Guid = "9d28782f-e341-4665-9230-34b8f138e11f")]
    public readonly InputSlot<int> Port = new();

    [Input(Guid = "2d750c55-1168-4afd-89c4-7810dc1dda58")]
    public readonly InputSlot<bool> Listen = new();
}
