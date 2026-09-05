namespace Skills.Render.R05_ShadingAndMaterial;

[Guid("e362db76-d60b-4dc3-8097-6eda71b525e9")]
internal sealed class R05d_MultiChannelTextures :Instance<R05d_MultiChannelTextures>{
    [Output(Guid = "1a017204-e719-4d18-b790-93c94d2bea4b")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}