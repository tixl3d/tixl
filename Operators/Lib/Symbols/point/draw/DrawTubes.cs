namespace Lib.point.draw;

[Guid("c12cf584-f6db-4d24-a03a-7801736d2c50")]
internal sealed class DrawTubes : Instance<DrawTubes>
{
    [Output(Guid = "dab46419-6502-442e-a6c7-30f3bb882be4")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "0a6a91d1-be44-459a-94cd-49b48d755377")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "ae484c37-1bf0-4e20-8698-3f7179ab7c24")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "02f5e531-2579-4eca-8fef-a8586e6534cf")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "c3742de6-6720-4a18-a6da-063e05696f9d")]
    public readonly InputSlot<float> Spin = new();

    [Input(Guid = "8f609301-338d-45e0-82de-660963ec0174")]
    public readonly InputSlot<float> Twist = new();

    [Input(Guid = "bdf36fc7-cbaf-48f5-ab41-d903036e7d46")]
    public readonly InputSlot<int> TextureMode = new();

    [Input(Guid = "e1f3945d-1ab8-4e6c-b5ca-c5036ed7d52a")]
    public readonly InputSlot<Vector2> TextureRange = new();

    [Input(Guid = "b1bffdfb-fc45-4ec1-baac-39a3ef2f065a")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "ec6a8011-f1da-413b-a9e4-f909859444b5")]
    public readonly InputSlot<int> BlendMod = new();

    [Input(Guid = "9a486753-840e-4d53-9627-8a2ed02fd39e")]
    public readonly InputSlot<CullMode> Culling = new();

    [Input(Guid = "c43b1052-2942-43c7-aaf4-56c91dc8e521")]
    public readonly InputSlot<bool> UseWAsWeight = new();

    [Input(Guid = "1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d")]
    public readonly InputSlot<bool> UseScale = new();

    [Input(Guid = "f8fc2813-2156-4ffd-a546-38214b887e87")]
    public readonly InputSlot<int> Sides = new();

    [Input(Guid = "4876fa78-b008-42d9-83bf-ae0f577d5e5c")]
    public readonly InputSlot<bool> CapStart = new();

    [Input(Guid = "740ee4c7-ea29-45ee-aebc-59d74627ed31")]
    public readonly InputSlot<bool> CapEnd = new();

    [Input(Guid = "d4e5f6a7-b8c9-4d1e-2f3a-4b5c6d7e8f90")]
    public readonly InputSlot<bool> Smooth = new();

    [Input(Guid = "a7b8c9d0-e1f2-4a3b-5c6d-7e8f90123456")]
    public readonly InputSlot<float> RoundAmount = new();

    [Input(Guid = "b8c9d0e1-f2a3-4b5c-6d7e-8f9012345678")]
    public readonly InputSlot<int> SubSegments = new();

    [Input(Guid = "a0b1c2d3-e4f5-4a6b-8c7d-9e0f1a2b3c4d")]
    public readonly InputSlot<bool> DistanceScale = new();

    [Input(Guid = "b1c2d3e4-f5a6-4b7c-9d8e-0f1a2b3c4d5e")]
    public readonly InputSlot<float> ScaleNearDist = new();

    [Input(Guid = "c2d3e4f5-a6b7-4c8d-0e9f-1a2b3c4d5e6f")]
    public readonly InputSlot<float> ScaleFarDist = new();

    [Input(Guid = "d3e4f5a6-b7c8-4d9e-1f0a-2b3c4d5e6f70")]
    public readonly InputSlot<float> MinScale = new();
        
    private enum TextureModes
    {
        RelativeStartEnd,
        StartRepeat,
        Tile,
        UseW,
    }
}