namespace Examples.Lib.physics;

/// <summary>
/// A torus volume pulls 300 boxes onto its surface, with gravity turned off.
/// </summary>
[Guid("0f12b1a9-7c69-51d1-ab4a-ad4778d97aeb")]
internal sealed class PhysicsVolumeGravityExample : Instance<PhysicsVolumeGravityExample>
{
    [Output(Guid = "707067e2-92d8-53b7-b124-13a032211f4b")]
    public readonly Slot<Texture2D> Output = new();
}
