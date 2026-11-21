namespace Examples.Lib.physics;

/// <summary>
/// A pile of falling balls wrapped in one smooth skin through a volume.
/// </summary>
[Guid("faa1fe57-3dd8-5136-93c3-fe9bdbeccd4d")]
internal sealed class PhysicsToVolumeExample : Instance<PhysicsToVolumeExample>
{
    [Output(Guid = "6aa0bc05-b707-559a-8862-7ddc3b359fa0")]
    public readonly Slot<Texture2D> Output = new();
}
