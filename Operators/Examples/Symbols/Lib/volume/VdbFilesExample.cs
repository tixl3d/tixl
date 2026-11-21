namespace Examples.Lib.volume;

/// <summary>
/// OpenVDB sample models and an animated EmberGen fire, loaded from a folder of .vdb files.
/// </summary>
[Guid("3d45bab4-aef9-507c-b213-348b0614a097")]
internal sealed class VdbFilesExample : Instance<VdbFilesExample>
{
    [Output(Guid = "aecae997-05bc-5876-926b-5e48aa8f62a3")]
    public readonly Slot<Texture2D> Output = new();
}
