namespace Lib.image.use;

[Guid("085b7841-9502-4b49-896e-3b1fa023f1bf")]
internal sealed class CombineMaterialChannels : Instance<CombineMaterialChannels>
{
    [Output(Guid = "47eea3ed-f553-47ad-b292-1c3f08f697f7")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "c697d838-f9a3-4ee7-af3d-713494f0ae93")]
    public readonly InputSlot<Texture2D> Roughness = new();

    [Input(Guid = "ae7df1c8-ffd1-4a32-9f25-769fc7630f6f")]
    public readonly InputSlot<Texture2D> Metallic = new();

    [Input(Guid = "55ee3c9c-ffbc-4322-bb44-69d1b7001ff8")]
    public readonly InputSlot<Texture2D> Occlusion = new();

    [Input(Guid = "34ba88b4-7fe0-4f40-9433-feab3b6e81f0")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "16522565-e9aa-4295-b219-9724d656ced3")]
    public readonly InputSlot<bool> GenerateMips = new();

    [Input(Guid = "099eae25-7cca-4da2-956f-c1a5bd67e764")]
    public readonly InputSlot<Curve> RemapRoughness = new();
}