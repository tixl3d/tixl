namespace Examples.Lib.physics;

/// <summary>
/// Bodies made from geometry: concave rings and letters that collide as what they are.
/// </summary>
[Guid("db613897-4f88-5c81-a321-c460456d3001")]
internal sealed class RigidBodyExample : Instance<RigidBodyExample>
{
    [Output(Guid = "1086c26e-037c-596b-b7b8-cd9be710528a")]
    public readonly Slot<Texture2D> Output = new();
}
