namespace Lib.render.shading;

[Guid("9d71d46c-f3d8-4bf4-a104-38c0b37cc88b")]
internal sealed class Equirectangle : Instance<Equirectangle>
{

    [Output(Guid = "52dacae9-3407-4748-adb3-dc691178e9bc")]
    public readonly Slot<Texture2D> OutputColor = new Slot<Texture2D>();

    [Input(Guid = "2097d7c0-604b-4909-8cf1-ca9793dc53ec")]
    public readonly InputSlot<Command> InputCommand = new InputSlot<Command>();

    [Input(Guid = "9e35ef34-0b02-40f6-93d5-5163346d681a")]
    public readonly InputSlot<int> Dimension = new InputSlot<int>();

    [Output(Guid = "bbc85c64-3d0a-47ce-8126-9f90d4b60fac")]
    public readonly Slot<Texture2D> OutputDepth = new Slot<Texture2D>();


}