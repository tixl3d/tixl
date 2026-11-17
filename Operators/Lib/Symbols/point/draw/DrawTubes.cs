using T3.Core.Utils;

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

    [Input(Guid = "c43b1052-2942-43c7-aaf4-56c91dc8e521", MappedType = typeof(WidthFXs))]
    public readonly InputSlot<int> WidthFactor = new();

    [Input(Guid = "1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d")]
    public readonly InputSlot<bool> UsePointScale = new();

    [Input(Guid = "f8fc2813-2156-4ffd-a546-38214b887e87")]
    public readonly InputSlot<int> Sides = new();

    [Input(Guid = "c3742de6-6720-4a18-a6da-063e05696f9d")]
    public readonly InputSlot<float> Spin = new();

    [Input(Guid = "8f609301-338d-45e0-82de-660963ec0174")]
    public readonly InputSlot<float> Twist = new();

    [Input(Guid = "4b0ae46d-6798-4c5b-aaa0-2cabb83138dd", MappedType = typeof(TextureModes))]
    public readonly InputSlot<int> TextureMode = new();

    [Input(Guid = "0a27f15b-81cc-473d-abb3-a7e85b4c03f7")]
    public readonly InputSlot<Vector2> TextureRange = new();

    [Input(Guid = "51341757-a93b-4ca8-9e8d-be30124fc060")]
    public readonly InputSlot<float> FlatAround = new();

    [Input(Guid = "c7562da2-b723-46fc-9f28-e12f7ed52bb0")]
    public readonly InputSlot<float> FlatAlong = new();

    [Input(Guid = "2d848beb-c753-4f9d-be08-097a3b1ab5a1")]
    public readonly InputSlot<float> Miter = new();

    [Input(Guid = "d74dafc3-69d5-4783-867c-edc9c4a0dc93")]
    public readonly InputSlot<bool> CloseEnds = new();

    [Input(Guid = "196458c8-dfbc-4f23-829c-6c3549b8a5cf")]
    public readonly InputSlot<float> CapInset = new();

    [Input(Guid = "b1bffdfb-fc45-4ec1-baac-39a3ef2f065a")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "ec6a8011-f1da-413b-a9e4-f909859444b5", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "9a486753-840e-4d53-9627-8a2ed02fd39e")]
    public readonly InputSlot<CullMode> Culling = new();

    private enum WidthFXs
    {
        None,
        F1,
        F2,
    }

    private enum TextureModes
    {
        Order,
        F1,
        F2,
        Distance,
        DistanceNormalized,
        ContinuousDistance,
        ContinuousDistanceNormalized,
    }
}
