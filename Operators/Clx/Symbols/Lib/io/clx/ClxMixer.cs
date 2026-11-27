using Clockworks.CLX;
using Clx.Utils;
using T3.Core.Animation;

namespace Lib.io.clx;

[Guid("20390db1-e88a-4adb-8ef0-e0a7de2e0012")]
internal sealed class ClxMixer : Instance<ClxMixer>, IStatusProvider
{
    [Output(Guid = "b5ae63e9-d5a9-4ea5-ba9b-8de18b19a691", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Upfader = new();

    [Output(Guid = "cc397653-7bf6-48f5-ae6c-b609c4825bf7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Upfaders = new();

    [Output(Guid = "4ab3d60d-5f65-4eca-86ef-3d5ff55d9aac", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Crossfader = new();

    [Output(Guid = "dd92ff27-9cac-469d-a1a2-e8d1545c2df5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> ActiveDeck = new();

    [Output(Guid = "0a9963b7-9818-444c-b939-283b97dd3044", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> AppState = new();

    [Output(Guid = "3c68544a-0007-4e4d-9ff8-056ca5169636", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasData = new();

    public ClxMixer()
    {
        Upfader.UpdateAction += Update;
        Upfaders.UpdateAction += Update;
        Crossfader.UpdateAction += Update;
        ActiveDeck.UpdateAction += Update;
        AppState.UpdateAction += Update;
        HasData.UpdateAction += Update;
        Upfaders.Value = _upfaders;
    }

    private void Update(EvaluationContext context)
    {
        if (Playback.FrameCount == _lastFrame)
            return;

        _lastFrame = Playback.FrameCount;

        var client = _binding.Update(Port.GetValue(context), Listen.GetValue(context));
        var hasData = client != null && client.TryGetControl(out _packet);
        HasData.Value = hasData;
        if (!hasData)
            return;

        _upfaders.Clear();
        for (var deck = 1; deck <= 6; deck++)
            _upfaders.Add((float)_packet.GetUpfader(deck));

        Upfaders.Value = _upfaders;
        Upfader.Value = (float)_packet.GetUpfader(Deck.GetValue(context));
        Crossfader.Value = (float)_packet.Crossfader;
        ActiveDeck.Value = _packet.Active;
        AppState.Value = _packet.AppState ?? string.Empty;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            _binding.Release();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _binding.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _binding.GetStatusMessage();

    private readonly ClxBinding _binding = new();
    private readonly List<float> _upfaders = new(6);
    private ControlPacket _packet;
    private int _lastFrame = -1;

    [Input(Guid = "021044b0-6fcf-4364-8726-5fa478cfa316")]
    public readonly InputSlot<int> Deck = new();

    [Input(Guid = "a9cd3e51-4085-4b29-8525-d9e82b7942a4")]
    public readonly InputSlot<int> Port = new();

    [Input(Guid = "d535762c-c8ae-4fef-9005-a57838ff624f")]
    public readonly InputSlot<bool> Listen = new();
}
