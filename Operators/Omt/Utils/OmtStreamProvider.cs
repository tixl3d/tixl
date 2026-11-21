#nullable enable
using T3.Core.Output.Streaming;

namespace Lib.Utils;

/// <summary>Offers OMT senders to the output setup; discovered by type when this package loads.</summary>
/// <remarks>Declares the codec so an installation that only streams, with no OMT operator in its graph, still
/// ships it.</remarks>
[ExportDependencies("libomtnet.dll", "libvmx.dll", "libvmx.so", "libvmx.dylib")]
internal sealed class OmtStreamProvider : IOutputStreamProvider
{
    public string Kind => "OMT";

    public OutputStreamOptions Supported => OutputStreamOptions.FrameRate | OutputStreamOptions.Alpha;

    public IOutputStreamSender CreateSender(string name) => new OmtSender(name);
}
