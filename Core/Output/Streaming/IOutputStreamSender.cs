#nullable enable
using System;
using T3.Core.DataTypes;

namespace T3.Core.Output.Streaming;

/// <summary>A live sender: the host pushes each presented frame; the sender owns its native resources.</summary>
public interface IOutputStreamSender : IDisposable
{
    string Name { get; }

    /// <summary>Applies the plug's settings; called before each <see cref="Send"/>, so an edit takes effect at
    /// once. A sender ignores whatever its transport has no notion of.</summary>
    void Configure(OutputStreamSettings settings);

    /// <summary>Sends one frame. False when nothing was sent (no receiver, unsupported format, native failure);
    /// <see cref="LastError"/> then says why, if the sender knows.</summary>
    bool Send(Texture2D texture);

    string? LastError { get; }

    /// <summary>
    /// The address a receiver selects in this stream, when the transport has a discoverable one (OMT). Null for
    /// transports that are found by name only, and before the sender has opened.
    /// </summary>
    string? AdvertisedAddress => null;

    /// <summary>Opens the sender's endpoint without sending a frame; called each frame on a warm failover standby.</summary>
    void KeepWarm()
    {
    }
}
