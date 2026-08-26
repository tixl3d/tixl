namespace Lib.point.draw;

[Guid("8eb09af4-d3c5-4fcc-b7f9-5d9367a02a2f")]
internal sealed class DrawPoints2 : Instance<DrawPoints2>
{
    [Output(Guid = "d5f9af38-0b67-42e3-b175-40a34a4dce00")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "57abf18b-7e93-4c59-acbc-9d4bb04a8ae3")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "2664b05a-9fb3-4c59-b85a-8f460c60df45")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "2a4c93e7-674b-4046-9c0d-2397e9a2d8dd")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "df0ed524-d094-47e0-b974-c936a7ea8ee1")]
    public readonly InputSlot<Texture2D> Texture_ = new();

    [Input(Guid = "84e512f6-6dca-4c56-bcd8-a08e596a42bf")]
    public readonly InputSlot<bool> EnableZWrite = new();

    [Input(Guid = "8079d0b6-728d-4d04-a7c4-5d38da6663ac")]
    public readonly InputSlot<bool> EnableZTest = new();

    [Input(Guid = "05e30609-d380-446b-8fba-f930e95e2281")]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "57343cad-5b79-4b1d-be83-03c5debdf504")]
    public readonly InputSlot<float> FadeNearest = new();

    [Input(Guid = "6b9f85bc-b9d4-4285-bd9f-f2fcc3130835")]
    public readonly InputSlot<bool> UseWForSize = new();
}