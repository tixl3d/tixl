using Clockworks.CLX;

namespace Clx.Utils;

internal static class ClxTrackLength
{
    /// <summary>
    /// A deck's track length in seconds: from its Deck packets, or from its waveform for LITE senders,
    /// which send 0 for Length. 0 when neither is known.
    /// </summary>
    internal static double Get(CLXClient client, int deck)
    {
        if (client == null)
            return 0;

        if (client.TryGetDeck(deck, out var packet) && packet.Length > 0)
            return packet.Length;

        return client.GetWaveform(deck)?.Duration ?? 0;
    }
}
