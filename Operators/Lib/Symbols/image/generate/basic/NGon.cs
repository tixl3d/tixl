using T3.Core.Utils;

namespace Lib.image.generate.basic;

[Guid("22a3cd4e-02b3-44d7-ad2b-aab5810c5e88")]
internal sealed class NGon : Instance<NGon>
{
    [Output(Guid = "2b217712-b13e-4335-8aa1-ccb6578dade7")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "837a9689-d7a8-43db-88a5-2ac3ce8fbd37")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "1e13694f-18ad-4cd7-8cae-a5c692904edc")]
    public readonly InputSlot<Vector4> Fill = new InputSlot<Vector4>();

    [Input(Guid = "a3dcbd9b-63ea-45dc-b761-7b4f6ddcba14")]
    public readonly InputSlot<Vector4> Background = new InputSlot<Vector4>();

    [Input(Guid = "e7a99144-1f59-42ae-bdac-906ca1e54e0d")]
    public readonly InputSlot<float> Sides = new InputSlot<float>();

    [Input(Guid = "c6c3d50e-9731-42cc-a0fc-ee8d15ac6cca")]
    public readonly InputSlot<float> Radius = new InputSlot<float>();

    [Input(Guid = "7428e03f-1f6f-4be4-adb9-accef49b64a6")]
    public readonly InputSlot<float> Curvature = new InputSlot<float>();

    [Input(Guid = "9a857159-9593-490a-9ef5-1c66c6c6b68d")]
    public readonly InputSlot<float> Blades = new InputSlot<float>();

    [Input(Guid = "4d9bfddb-901e-4524-a99c-ba594d317e8a")]
    public readonly InputSlot<float> Feather = new InputSlot<float>();

    [Input(Guid = "4ac28e13-fac9-4f1a-9aa7-e564a0d57e93")]
    public readonly InputSlot<float> Round = new InputSlot<float>();

    [Input(Guid = "1df7ac79-b5c8-4f51-80e0-5f6503c3f158")]
    public readonly InputSlot<float> FeatherBias = new InputSlot<float>();

    [Input(Guid = "ac8bbd32-bf05-489f-a2b8-3b11f68704f8")]
    public readonly InputSlot<Vector2> Position = new InputSlot<Vector2>();

    [Input(Guid = "7613bc20-d400-440d-b6a5-5f60ce61a33c")]
    public readonly InputSlot<float> Rotate = new InputSlot<float>();

    [Input(Guid = "276431d2-e689-41f9-9f73-5544f9368a53")]
    public readonly InputSlot<Int2> Resolution = new InputSlot<Int2>();

    [Input(Guid = "f315a8c4-9d9b-41a4-a4b8-81d7fc667dee", MappedType = typeof(SharedEnums.RgbBlendModes))]
    public readonly InputSlot<int> BlendMode = new InputSlot<int>();
}