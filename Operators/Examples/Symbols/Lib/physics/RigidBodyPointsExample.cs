namespace Examples.Lib.physics;

/// <summary>
/// A car on four motorised wheels drives into a wall of crates drawn as instanced meshes.
/// </summary>
[Guid("130a7ff9-33be-5377-8ea1-c7fffe6adfb1")]
internal sealed class RigidBodyPointsExample : Instance<RigidBodyPointsExample>
{
    [Output(Guid = "89d9cb1a-0606-5afc-bf3e-096412913cf2")]
    public readonly Slot<Texture2D> Output = new();
}
