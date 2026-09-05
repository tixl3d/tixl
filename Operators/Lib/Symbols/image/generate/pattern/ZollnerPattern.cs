namespace Lib.image.generate.pattern;

[Guid("f2a0a8d9-e2dc-476b-ac97-de13101a6fdb")]
internal sealed class ZollnerPattern : Instance<ZollnerPattern>
{
    [Output(Guid = "16393dfd-962c-4dbb-b698-c2099143d98c")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "b68794e6-4067-4500-b8ba-7cb6f5e9ea28")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "bc362c49-5a25-4b62-b385-ce18b1f79077")]
    public readonly InputSlot<Vector4> Fill = new();

    [Input(Guid = "99c7e849-6305-423d-9042-5853e789a572")]
    public readonly InputSlot<Vector4> Background = new();

    [Input(Guid = "23782414-01b3-437a-ab6f-99e1770c4126")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "4bdef8db-3057-4e45-a7f8-b1c2e06cb1a5")]
    public readonly InputSlot<Vector2> Offset = new();

    [Input(Guid = "ecaf4537-146d-4765-8b88-b5a33f5544a1")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "da1779e6-36fd-4b5d-9730-3f3da244ca40")]
    public readonly InputSlot<float> Rotate = new();

    [Input(Guid = "44e1a24c-ee74-4fcf-8704-f6f2b1d567b0")]
    public readonly InputSlot<float> Feather = new();

    [Input(Guid = "d634a626-7552-4cd1-8bf2-6eb09a582d52")]
    public readonly InputSlot<float> BarWidth = new();

    [Input(Guid = "a2765fe8-c65a-4510-9e07-0c76521506bd")]
    public readonly InputSlot<float> HookRotation = new();

    [Input(Guid = "71765e67-2057-4819-b406-0382c1ab0893")]
    public readonly InputSlot<float> HookLength = new();

    [Input(Guid = "901206a2-c432-41c7-910b-f2f419b913af")]
    public readonly InputSlot<float> HookWidth = new();

    [Input(Guid = "68bbb3ac-e8f2-4ed9-afd8-8a912c97f1ca")]
    public readonly InputSlot<float> RowSwift = new();

    [Input(Guid = "3ee9d4c7-8f6d-453a-a6ff-262250e6559a")]
    public readonly InputSlot<float> RAffects_BarWidth = new();

    [Input(Guid = "fb4ed3f0-9a3a-41e2-9e49-2cd8ff5221e2")]
    public readonly InputSlot<float> GAffects_HookLength = new();

    [Input(Guid = "8f8a92bf-cdc6-45cd-9ff7-5d01cd31bd8e")]
    public readonly InputSlot<float> BAffects_HookRotation = new();

    [Input(Guid = "c409ea13-e805-4e37-94d6-f3633810f906")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "98131a97-a883-473f-809a-1c4d9eabce63")]
    public readonly InputSlot<float> AmplifyIllusion = new();
}