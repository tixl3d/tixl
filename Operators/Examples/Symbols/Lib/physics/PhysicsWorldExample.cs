namespace Examples.Lib.physics;

/// <summary>
/// Physics basics: bodies, materials, a domino run and contact sparks in one world.
/// </summary>
[Guid("d05d13f9-1270-5855-a6d3-c98667892977")]
internal sealed class PhysicsWorldExample : Instance<PhysicsWorldExample>
{
    [Output(Guid = "d662b16a-0158-505d-b4f3-c195759ccd76")]
    public readonly Slot<Texture2D> Output = new();
}
