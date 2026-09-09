using T3.Core.Output;

namespace Lib.Utils;

/// <summary>Offers Spout senders to the output setup; discovered by type when this package loads.</summary>
internal sealed class SpoutStreamProvider : IOutputStreamProvider
{
    public string Kind => "Spout";

    public IOutputStreamSender CreateSender(string name) => new SpoutSender(name);
}
