#nullable enable
namespace T3.Core.Output.Streaming;

/// <summary>
/// A kind of network/IPC video stream this machine can send (Spout, NDI, …). Implemented by operator packages
/// that carry the native library; discovered by type when the package assembly loads, so the host can offer a
/// stream plug without referencing the package. One instance per loaded package assembly.
/// </summary>
public interface IOutputStreamProvider
{
    /// <summary>Stable identifier persisted in machine configs (e.g. "Spout", "NDI").</summary>
    string Kind { get; }

    /// <summary>The settings senders of this kind honour; the rest are not offered for it.</summary>
    OutputStreamOptions Supported { get; }

    /// <summary>Opens a sender under the given name. Frame size follows the textures sent.</summary>
    IOutputStreamSender CreateSender(string name);

    /// <summary>
    /// Whether a sender may stay open, sending nothing, while this machine is a show-failover standby. The source
    /// is then already on the network when the active machine fails, so receivers that fail over to it connect at
    /// once instead of waiting for it to appear. Off for transports that would keep re-sending their last frame.
    /// </summary>
    bool SupportsWarmStandby => false;
}
