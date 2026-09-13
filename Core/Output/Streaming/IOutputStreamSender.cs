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
}
