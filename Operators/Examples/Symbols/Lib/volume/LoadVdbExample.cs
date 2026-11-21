namespace Examples.Lib.volume;

/// <summary>
/// Volumes from files: the Stanford bunny as a mesh and a smoke plume raymarched as density.
/// </summary>
[Guid("ff0e1abc-733f-59ff-9af5-3e0382474bd6")]
internal sealed class LoadVdbExample : Instance<LoadVdbExample>
{
    [Output(Guid = "7eec62d6-6b20-55a4-b813-6ad4a7a7ade9")]
    public readonly Slot<Texture2D> Output = new();
}
