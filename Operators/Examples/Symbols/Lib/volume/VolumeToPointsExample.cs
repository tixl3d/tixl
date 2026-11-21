namespace Examples.Lib.volume;

/// <summary>
/// Three ways out of a volume: a mesh, a point cloud with its sparse tree, and a raymarched texture.
/// </summary>
[Guid("91bb0f8b-593c-557c-9da6-15005722b733")]
internal sealed class VolumeToPointsExample : Instance<VolumeToPointsExample>
{
    [Output(Guid = "267e084f-5393-5b46-83b0-c8c4c901120c")]
    public readonly Slot<Texture2D> Output = new();
}
