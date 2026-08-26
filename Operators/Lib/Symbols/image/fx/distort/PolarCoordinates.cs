namespace Lib.image.fx.distort;

[Guid("941410e5-2023-43c6-87b1-55386bb048ac")]
internal sealed class PolarCoordinates : Instance<PolarCoordinates>
{
    [Output(Guid = "1758e789-809c-430a-a5c8-22fd8bbe5e54")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "dfa1d71d-2964-41c2-bb04-86a39c36ce6e")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "4c07b8bd-c78d-44ce-bb95-8114284af2bf")]
    public readonly InputSlot<Vector2> Center = new();

    [Input(Guid = "83f2dc32-5830-4558-9137-f6805f3f6ff6")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "d796cbc2-26b4-4805-b1b7-bd23b42d1984")]
    public readonly InputSlot<float> RadialBias = new();

    [Input(Guid = "6f17f1b4-0b5e-4934-8be1-c8198443fac3")]
    public readonly InputSlot<float> RadialOffset = new();

    [Input(Guid = "627847c4-5ba3-4852-bb09-2b0d99e05451")]
    public readonly InputSlot<float> Twist = new();

    [Input(Guid = "4bd38d2f-0c52-456f-9938-b1f571c719fe")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "caa14f08-f004-4af8-a448-116e21106d5b")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "bfcb7f48-517a-4791-96fa-4aa26862839b", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    private enum Modes
    {
        Cartesian2Polar,
        Polar2Cartesian,
    }
}