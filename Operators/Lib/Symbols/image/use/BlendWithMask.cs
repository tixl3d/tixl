namespace Lib.image.use;

[Guid("7da55d23-0bd1-457b-a036-9b6b51d2e34b")]
internal sealed class BlendWithMask : Instance<BlendWithMask>
{
    [Output(Guid = "dcf13066-95dc-4c0f-8c8c-379f396502ce")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "7d878133-43cf-44a3-87a2-18d44f74f17d")]
    public readonly InputSlot<Texture2D> ImageA = new();

    [Input(Guid = "0f542667-8b2c-4fd7-9f9a-d63f1573d25a")]
    public readonly InputSlot<Vector4> ColorA = new();

    [Input(Guid = "c68c887c-16f1-4fa2-89a4-4a07d44a0df6")]
    public readonly InputSlot<Texture2D> ImageB = new();

    [Input(Guid = "f551d82e-bbd5-40fd-9d84-e08d97f06c46")]
    public readonly InputSlot<Vector4> ColorB = new();

    [Input(Guid = "d08813be-bd43-4229-86b7-4e53b62b8561")]
    public readonly InputSlot<Texture2D> Mask = new();

    [Input(Guid = "ff0e3f81-1340-40e7-9c95-b88938d63901")]
    public readonly InputSlot<Int2> Resolution = new();

}