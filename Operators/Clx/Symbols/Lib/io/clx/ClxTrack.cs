using Clockworks.CLX;
using Clx.Utils;
using T3.Core.Animation;

namespace Lib.io.clx;

[Guid("17f4b56c-a250-4e27-9f87-7c37b58749b9")]
internal sealed class ClxTrack : Instance<ClxTrack>, IStatusProvider
{
    [Output(Guid = "c03ad78d-6256-41a8-a2c6-6d840cd71037", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> Title = new();

    [Output(Guid = "0fd44e68-7dab-4ee3-9381-4ebe6a90b091", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> Artist = new();

    [Output(Guid = "51b595b1-7722-45ab-957f-034efedb3b85", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> Album = new();

    [Output(Guid = "5d78b332-5b6e-4902-88ac-c01261308dd1", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> FilePath = new();

    [Output(Guid = "d586a17f-e1d0-4edd-ab0e-d540bfb9f95a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> TrackHash = new();

    [Output(Guid = "c87d2929-894f-409d-afab-fd54c6787811", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> TrackChanged = new();

    [Output(Guid = "bce715c6-1c40-4aef-9235-09e76a32bc70", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasData = new();

    public ClxTrack()
    {
        Title.UpdateAction += Update;
        Artist.UpdateAction += Update;
        Album.UpdateAction += Update;
        FilePath.UpdateAction += Update;
        TrackHash.UpdateAction += Update;
        TrackChanged.UpdateAction += Update;
        HasData.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Playback.FrameCount == _lastFrame)
            return;

        _lastFrame = Playback.FrameCount;
        TrackChanged.Value = false;

        var deck = Deck.GetValue(context);
        var client = _binding.Update(Port.GetValue(context), Listen.GetValue(context));
        var hasData = client != null && client.TryGetMeta(deck, out _packet);
        HasData.Value = hasData;
        if (!hasData)
            return;

        Title.Value = _packet.Title ?? string.Empty;
        Artist.Value = _packet.Artist ?? string.Empty;
        Album.Value = _packet.Album ?? string.Empty;
        FilePath.Value = _packet.FilePath ?? string.Empty;
        TrackHash.Value = _packet.TrackHash;

        // A pulse for one frame whenever the deck loads another track, but not when this
        // operator first sees the deck or switches to another one.
        var changeCount = _binding.Connection.GetTrackChangeCount(deck);
        if (deck == _lastDeck && changeCount != _lastChangeCount)
            TrackChanged.Value = true;

        _lastDeck = deck;
        _lastChangeCount = changeCount;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            _binding.Release();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _binding.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _binding.GetStatusMessage();

    private readonly ClxBinding _binding = new();
    private MetaPacket _packet;
    private int _lastFrame = -1;
    private int _lastDeck = -1;
    private int _lastChangeCount;

    [Input(Guid = "cebf449e-48bf-481b-a78c-19c6f2eb3329")]
    public readonly InputSlot<int> Deck = new();

    [Input(Guid = "49fa4d91-aa1a-4e8f-babc-0cb3e7588fa8")]
    public readonly InputSlot<int> Port = new();

    [Input(Guid = "5a11e8ec-3ab6-448a-be9a-8b16f98fba54")]
    public readonly InputSlot<bool> Listen = new();
}
