namespace Lib.render.postfx;

[Guid("13d5260d-4e50-48f8-909c-7d84d6f0a43f")]
internal sealed class SSAO : Instance<SSAO>
{
    [Output(Guid = "9be415b6-b7f0-4b8f-8d93-c147ef8d0d44")]
    public readonly Slot<Texture2D> Output = new();

    [Output(Guid = "ac91f2bf-9162-4c4d-a8fd-865f961cfac9")]
    public readonly Slot<Texture2D> DepthBuffer2 = new();

    [Input(Guid = "450023da-53fb-4e36-9936-551a2ebcce84")]
    public readonly InputSlot<Texture2D> Texture2d = new();

    [Input(Guid = "938b9656-f0ad-4f4c-be45-e56185f7a94a")]
    public readonly InputSlot<Texture2D> DepthBuffer = new();

    [Input(Guid = "602f3004-59ef-4065-a9a6-3c14fab79c6c")]
    public readonly InputSlot<Vector2> NearFarRange = new();

    [Input(Guid = "40e22707-5c08-4c64-adcb-080b50aeb4f6")]
    public readonly InputSlot<Vector2> NearFarClip = new();

    [Input(Guid = "12cd23ef-82ea-41b3-a8da-72bb66018d86")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "e54a0202-19ab-42f8-bd7d-ae2881d58ea2")]
    public readonly InputSlot<Vector2> BoostShadows = new();

    [Input(Guid = "c310f731-bc13-44e7-a827-d8e4f996ff99")]
    public readonly InputSlot<float> Passes = new();

    [Input(Guid = "592b84f4-e53c-499a-ab83-150e0eabfcdf")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "9ac4806b-bbcf-4b29-ac15-4bc7f52ef192")]
    public readonly InputSlot<float> MixOriginal = new();

    [Input(Guid = "0fb4d665-3014-48a4-b544-c5d28d699fc4")]
    public readonly InputSlot<float> MultiplyOriginal = new();

    [Input(Guid = "c7c1d642-851e-41a8-895d-df28b5bb770e")]
    public readonly InputSlot<Vector2> NoiseOffset = new();

}