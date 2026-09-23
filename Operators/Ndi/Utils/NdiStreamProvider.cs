#nullable enable
using T3.Core.Operator.Attributes;
using T3.Core.Output;
using T3.Core.Output.Streaming;

namespace Lib.Utils;

/// <summary>Offers NDI senders to the output setup; discovered by type when this package loads.</summary>
/// <remarks>Declares the NDI runtime so an installation that only streams, with no NDI operator in its graph,
/// still ships it.</remarks>
[ExportDependencies("NDILibDotNet6.dll", "Processing.NDI.Lib.x64.dll")]
internal sealed class NdiStreamProvider : IOutputStreamProvider
{
    public string Kind => "NDI";

    public OutputStreamOptions Supported => OutputStreamOptions.FrameRate | OutputStreamOptions.Alpha;

    public IOutputStreamSender CreateSender(string name) => new NdiSender(name);
}
