namespace Examples.Lib.physics;

/// <summary>
/// Volumes as colliders: the Stanford bunny from a VDB file and a bowl carved with CSG.
/// </summary>
[Guid("126bb660-5354-5e40-b211-86e92adebade")]
internal sealed class PhysicsVolumesExample : Instance<PhysicsVolumesExample>
{
    [Output(Guid = "773d43f3-7302-5079-b078-d098d8e6b531")]
    public readonly Slot<Texture2D> Output = new();
}
