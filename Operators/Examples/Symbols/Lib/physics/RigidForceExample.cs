namespace Examples.Lib.physics;

/// <summary>
/// Forces: a whirlwind lifts 300 boxes, and an impulse blasts them apart on demand.
/// </summary>
[Guid("e3cfa5d7-fff0-5f26-b8b3-32539f72c32b")]
internal sealed class RigidForceExample : Instance<RigidForceExample>
{
    [Output(Guid = "86feaaa3-c19d-5b42-aa68-8cf0a9dceca0")]
    public readonly Slot<Texture2D> Output = new();
}
