namespace Lib.image.transform;


[Guid("a29cf1c8-d9cd-4a5d-b06c-597cbeb5b33d")]
internal sealed class Crop : Instance<Crop>
{
    [Output(Guid = "0f7a4421-97d7-48d0-8a99-a7cc84356be2")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "a7fff052-42c2-40fb-938e-9fb9b6cfa591")]
    public readonly InputSlot<Texture2D> Texture2d = new();

    [Input(Guid = "eff02108-2335-46b5-95ba-4235b9a26349")]
    public readonly InputSlot<Int2> LeftRight = new();

    [Input(Guid = "db4c2d41-c369-4182-83fd-cb04aa04ec76")]
    public readonly InputSlot<Int2> TopBottom = new();

    [Input(Guid = "3b62379a-de94-4be1-8471-357710ba14c3")]
    public readonly InputSlot<Vector4> PaddingColor = new();

}