namespace Examples.Lib.physics;

/// <summary>
/// Joints: a hanging rope, a seesaw on a hinge and a net draped over a ball.
/// </summary>
[Guid("95f7abd4-5a03-5b5a-86b7-3adf0daac5ad")]
internal sealed class RigidJointExample : Instance<RigidJointExample>
{
    [Output(Guid = "c5e29ad8-acf1-5f34-9099-53e67507486c")]
    public readonly Slot<Texture2D> Output = new();
}
