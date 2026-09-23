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
}
