namespace Lib.image.generate.basic;

[Guid("bf7fcd04-0cf6-4518-86cc-48f74483d98d")]
internal sealed class RoundedRect : Instance<RoundedRect>
{
    [Output(Guid = "babd085b-5099-4ddb-87d6-ab9d983067f1")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "6b6887d4-ebaa-4489-b993-5027789ce1c7")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "a20d31a3-115f-4d38-a988-2a41f6e5cbea")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "d4c20bfc-3943-4c5d-a8f8-5175fa152e91")]
    public readonly InputSlot<Vector4> Background = new();

    [Input(Guid = "ee2f51f9-38bb-4613-b234-173218cb3aae")]
    public readonly InputSlot<Vector2> Position = new();

    [Input(Guid = "c608e4c4-c545-45ed-8e37-95a53f6871d0")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "d2401798-593b-4cc3-85d8-3570ddec1f2a")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "202340b4-0629-40a1-9399-0cda047df116")]
    public readonly InputSlot<float> Rotate = new();

    [Input(Guid = "ef3d0313-50a1-42d3-b3cb-e8ea1b93cd34")]
    public readonly InputSlot<float> Round = new();

    [Input(Guid = "d4f79203-401a-4f6e-8f83-74a3d61c0177")]
    public readonly InputSlot<Vector4> StrokeColor = new();

    [Input(Guid = "80bd2460-c28d-490c-981b-77a6e89f3983")]
    public readonly InputSlot<float> Stroke = new();

    [Input(Guid = "be43e160-f9a0-4991-b764-2b5e533b37cc")]
    public readonly InputSlot<float> Feather = new();

    [Input(Guid = "574e7268-27aa-46b6-a13f-8ab63f5990d9")]
    public readonly InputSlot<float> FeatherBias = new();

    [Input(Guid = "07e57992-ea38-4e3b-8d69-cb4e99ae587c")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "c9c66f57-5c8c-4481-9b9d-a8aa8569ac12")]
    public readonly InputSlot<bool> GenerateMips = new();
}