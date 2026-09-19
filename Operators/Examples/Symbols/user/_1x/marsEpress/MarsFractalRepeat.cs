namespace Examples.user._1x.marsEpress;

[Guid("92cdd8e1-5f31-4636-9ed3-f59a1e018586")]
 internal sealed class MarsFractalRepeat : Instance<MarsFractalRepeat>
{
    [Output(Guid = "786c3367-e7df-4811-a735-946e3b3d9ff3")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "a35335dd-b653-430b-9fa5-68ee48aef6c6")]
    public readonly Slot<T3.Core.DataTypes.BufferWithViews> TargetPoints = new();

    [Input(Guid = "2d8ea2ac-b7ce-4296-a606-6e6d74830763")]
    public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new();

    [Input(Guid = "499393f9-a751-4c72-b103-7fa849f9bbca")]
    public readonly InputSlot<float> Scaling = new();

    [Input(Guid = "59eb39d8-9708-4529-9dc6-32b7c43785a9")]
    public readonly InputSlot<System.Numerics.Vector3> Offset = new();

    [Input(Guid = "6c3c4830-cc8c-4a66-aa01-281fc0dafa16")]
    public readonly InputSlot<System.Numerics.Vector3> Rotation = new();


}