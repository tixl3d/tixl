#nullable enable
using T3.Core.Output;

namespace Lib.Utils;

/// <summary>Offers NDI senders to the output setup; discovered by type when this package loads.</summary>
internal sealed class NdiStreamProvider : IOutputStreamProvider
{
    public string Kind => "NDI";

    public OutputStreamOptions Supported => OutputStreamOptions.FrameRate | OutputStreamOptions.Alpha;

    public IOutputStreamSender CreateSender(string name) => new NdiSender(name);
}
