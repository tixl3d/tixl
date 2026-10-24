using T3.Core.Rendering.Material;

namespace Lib.field.render;

[Guid("0102bb5f-f874-4f4b-a833-c610c9e7fdf3")]
internal sealed class RaymarchToCubeMap : Instance<RaymarchToCubeMap>
{
    [Output(Guid = "6c747f6f-6d26-4362-a6e0-bc519c9fe0c7")]
    public readonly Slot<Texture2D> CubeMap = new();

    [Output(Guid = "b239b3b1-6417-4fc0-9e45-ebb3bd2d881b")]
    public readonly Slot<string> ShaderCode = new();

    [Input(Guid = "11fea42c-3529-4e04-aeb8-11d6cd566e8d")]
    public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> SdfField = new();

    [Input(Guid = "02ff65d1-0f72-4faa-b70c-2a32bb556f0d")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "64fbb6c8-08e9-4599-8615-1a7cbdd5f276")]
    public readonly InputSlot<Vector4> AmbientOcclusion = new();

    [Input(Guid = "5c700dc8-adee-4094-947e-b95e984dbe3f")]
    public readonly InputSlot<float> AoDistance = new();

    [Input(Guid = "fab31a66-baf8-4bed-b364-91dde35e0ef9")]
    public readonly InputSlot<float> TextureScale = new();

    [Input(Guid = "3bc4d148-6ace-4e75-b940-4e9498e8195c", MappedType = typeof(MappingModes))]
    public readonly InputSlot<int> UVMapping = new();

    [Input(Guid = "8a7b385c-2346-4ff9-94f7-7e8de315f5d6")]
    public readonly InputSlot<float> NormalSamplingD = new();

    [Input(Guid = "6c92f61f-cf24-4eaa-a72e-382b25b8aa07")]
    public readonly InputSlot<float> MaxSteps = new();

    [Input(Guid = "165a2e29-f25c-4c1d-965a-9e0b59cc01a7")]
    public readonly InputSlot<float> MinDistance = new();

    [Input(Guid = "cb08b59b-0863-4e87-aaf1-4e90962bffa0")]
    public readonly InputSlot<float> MaxDistance = new();

    [Input(Guid = "31298feb-7887-4fa7-8556-8f74e69b513d")]
    public readonly InputSlot<float> StepSize = new();

    [Input(Guid = "bc2060a0-edde-4691-86ae-1658e64abe02")]
    public readonly InputSlot<float> DistToColor = new();

    [Input(Guid = "fe0f0372-4a57-454e-9f6a-b65ad1d283a2")]
    public readonly InputSlot<float> SpecularAA = new();

    [Input(Guid = "9cec66d9-b274-4f34-89df-ec99994982e5")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "71641e7a-e7cb-43d5-af82-6a55d8d67b82")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "39c62c6b-911c-49ee-8f71-abb3a7fc4ad6", MappedType = typeof(Resolutions))]
    public readonly InputSlot<int> Resolution = new();

    [Input(Guid = "d62dc70b-700d-4aaf-aa1c-c716792184df")]
    public readonly InputSlot<Vector4> ClearColor = new();

    [Input(Guid = "90c4b4d6-ef09-48f1-ac8d-0d9763ce895a")]
    public readonly InputSlot<bool> GenerateMips = new();

    [Input(Guid = "e2d60235-bbea-45da-8f7a-9d11d76253a4")]
    public readonly InputSlot<PbrMaterial> Material = new();

    [Input(Guid = "982f00a7-b57f-4aa5-8784-0bb5b340c2be")]
    public readonly InputSlot<bool> EquiAngular = new();

    [Input(Guid = "94014f67-3e79-4a4b-b6ed-3fb59966ef6e", MappedType = typeof(TileSizes))]
    public readonly InputSlot<int> TileSize = new();

    [Input(Guid = "857855d9-713c-43e1-9bf5-6a8364269f30")]
    public readonly InputSlot<int> MaxSamples = new();

    [Input(Guid = "6c66b93f-0ab4-4a48-a1ad-e045fd3759bf")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "f4c0abdd-004f-4c9a-b062-044f94ee25b4")]
    public readonly InputSlot<float> NeighborhoodClipping = new();

    private enum MappingModes
    {
        Global_Triplanar,
        Local_Triplanar,
        Local_XY,
        Local_YZ,
        Local_XZ,
    }

    private enum TileSizes
    {
        _1x1 = 1,
        _2x2 = 2,
        _4x4 = 4,
        _8x8 = 8,
    }

    private enum Resolutions
    {
        _4 = 4,
        _16 = 16,
        _32 = 32,
        _64 = 64,
        _128 = 128,
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }
}
