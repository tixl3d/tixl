namespace Examples.Lib.point.draw;

[Guid("c1348a39-276f-4fe6-9210-f9f605cb0ece")]
 internal sealed class DrawBillboardsExample2 : Instance<DrawBillboardsExample2>
{
    [Output(Guid = "50b6aca4-2bfe-4008-a1cf-aa7065576fb2")]
    public readonly Slot<Texture2D> ColorBuffer = new();

    [Input(Guid = "384dfff2-bcb0-4e8d-aa83-ba1fe4ddc04e")]
    public readonly InputSlot<Texture2D> ImageInput = new();

    [Input(Guid = "9532a22f-4992-415b-a6da-d36e55f75690")]
    public readonly InputSlot<float> Hairiness = new();


}