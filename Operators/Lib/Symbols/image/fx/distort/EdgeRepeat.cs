namespace Lib.image.fx.distort;

[Guid("72e627e9-f570-4936-92b1-b12ed8d6004e")]
internal sealed class EdgeRepeat : Instance<EdgeRepeat>
{
    [Output(Guid = "f2e7625f-7918-4b0f-8b51-4304dde13bc6")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "22b41600-7d42-44ad-8e94-3819f8d24964")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "35e6faff-7e13-4fa8-8aca-32efc02f29e0")]
    public readonly InputSlot<Vector4> Fill = new();

    [Input(Guid = "b9859a88-4dd1-4221-9884-0a6e8cec9da4")]
    public readonly InputSlot<Vector4> Background = new();

    [Input(Guid = "549020f5-e405-4f78-a973-9e9fb3c9c25e")]
    public readonly InputSlot<Vector4> LineColor = new();

    [Input(Guid = "fb9e226b-866a-4228-bad9-bc7908bfc442")]
    public readonly InputSlot<Vector2> Center = new();

    [Input(Guid = "b77a3329-c938-4dd3-bf3b-8774db187ef5")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "b465481c-8b5d-4c6d-a659-6f51135b86fa")]
    public readonly InputSlot<float> Rotation = new();

    [Input(Guid = "881e04f7-1014-4e87-81c9-f4791086e57d")]
    public readonly InputSlot<float> LineThickness = new();

    [Input(Guid = "c30a5d00-681c-490d-80d4-d58577cd65db")]
    public readonly InputSlot<Int2> Resolution = new();
}