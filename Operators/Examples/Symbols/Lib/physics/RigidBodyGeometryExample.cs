namespace Examples.Lib.physics;

/// <summary>
/// Fracture: a cube shatters, and the collision hulls are shown next to the drawn geometry.
/// </summary>
[Guid("05fe32c1-2c83-5f9e-aaf9-0d5d7d2377ad")]
internal sealed class RigidBodyGeometryExample : Instance<RigidBodyGeometryExample>
{
    [Output(Guid = "00693f1f-3590-5eb1-bb29-898872222f37")]
    public readonly Slot<Texture2D> Output = new();
}
