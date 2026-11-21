namespace Examples.Lib.volume;

/// <summary>
/// Every volume primitive side by side: the building blocks of the volume package.
/// </summary>
[Guid("4f0e965c-96cd-5b62-9a47-1e4286b3367d")]
internal sealed class VolumePrimitiveExample : Instance<VolumePrimitiveExample>
{
    [Output(Guid = "5e0ad6f9-227e-5881-8920-a4a20836bc85")]
    public readonly Slot<Texture2D> Output = new();
}
