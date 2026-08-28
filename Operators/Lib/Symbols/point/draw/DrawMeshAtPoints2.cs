namespace Lib.point.draw;

[Guid("065f6edb-6d3d-414b-ab36-4620a957a18c")]
internal sealed class DrawMeshAtPoints2 : Instance<DrawMeshAtPoints2>
{
    [Output(Guid = "f3413673-aa50-489c-af70-af768206ccba")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "074ab4f7-8fab-4a98-b8d1-44c333648bf3")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "cb106a18-fd00-4696-bcfb-ef90957ff716")]
    public readonly InputSlot<Vector3> Stretch = new();

    [Input(Guid = "7af76083-a20e-4e10-9e52-e527085e2d1d")]
    public readonly InputSlot<bool> UseWForScale = new();

    [Input(Guid = "077a75d8-1928-4779-a587-9b8a1e5856ac")]
    public readonly InputSlot<Vector3> Offset = new();

    [Input(Guid = "8105e180-956a-4503-8ede-ac3c17e17e09", MappedType = typeof(Orientations))]
    public readonly InputSlot<int> Orientation = new();

    [Input(Guid = "ce0eef55-9242-479e-9592-2adff883ffd6")]
    public readonly InputSlot<float> RotateZ = new();

    [Input(Guid = "a1c7111e-a826-48e8-be09-b993af98de81")]
    public readonly InputSlot<Vector3> RotationAxis = new();

    [Input(Guid = "aa391a07-bff3-4087-85f3-ddaca3ac71c1")]
    public readonly InputSlot<float> Randomize = new();

    [Input(Guid = "65273d29-524b-40cc-bcd9-5b9e496e466b")]
    public readonly InputSlot<float> RandomPhase = new();

    [Input(Guid = "7aa26dc9-cea0-401e-85f4-160fe9a08e46")]
    public readonly InputSlot<Vector3> RandomPosition = new();

    [Input(Guid = "eb93c978-f0a1-47f7-b1ed-12da3b5db108")]
    public readonly InputSlot<float> RandomRotate = new();

    [Input(Guid = "2a85ffa9-d808-4907-8ac7-3c4bfbc51e1c")]
    public readonly InputSlot<float> RandomScale = new();

    [Input(Guid = "56a1cf7f-613a-41df-87f3-44562dca14a0")]
    public readonly InputSlot<Vector3> RandomStretch = new();

    [Input(Guid = "69caec2f-28b4-43be-8b96-06de813b25e1")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "1a87a03d-9f8f-479f-8681-f2a047b08f3b", MappedType = typeof(DistributionModes))]
    public readonly InputSlot<int> ColorVariationMode = new();

    [Input(Guid = "01288f85-aa38-4b63-b862-d40beacaeea2")]
    public readonly InputSlot<Gradient> ColorVariations = new();

    [Input(Guid = "44aa4ef1-15fd-4ab5-95e9-41a4ce6b42bf", MappedType = typeof(DistributionModes))]
    public readonly InputSlot<int> ScaleDistribution = new();

    [Input(Guid = "da61b784-0d00-40fb-882f-a0c2a8558fce")]
    public readonly InputSlot<Curve> Scales = new();

    [Input(Guid = "5344c8f2-2fe7-46b7-bf59-a74a264ab8b9")]
    public readonly InputSlot<float> SpreadLength = new();

    [Input(Guid = "8423dcdb-e2d0-447a-9eab-a3b4f40236c8")]
    public readonly InputSlot<float> SpreadPhase = new();

    [Input(Guid = "3be42b12-cdf0-4761-b22e-98b66b03fa39")]
    public readonly InputSlot<bool> SpreadPingPong = new();

    [Input(Guid = "a5c38a02-6d1b-4fc7-98ec-913eb3bd669e")]
    public readonly InputSlot<bool> SpreadRepeat = new();

    [Input(Guid = "fd1c7125-7e7c-4dc2-8407-441b7df7b2b9")]
    public readonly InputSlot<Texture2D> Texture_ = new();

    [Input(Guid = "a1b479af-bd24-496c-93e1-514b8bcae7ac")]
    public readonly InputSlot<int> AtlasMode = new();

    [Input(Guid = "4ee7bfd5-2f4d-4fcd-9bfd-026cfb0bfc38")]
    public readonly InputSlot<Int2> AtlasSize = new();

    [Input(Guid = "e71d2391-3ca3-4f04-b8d9-caefcb069285")]
    public readonly InputSlot<Texture2D> FxTexture = new();

    [Input(Guid = "a56b7c1a-9bb9-4f3d-a98d-dee72fd9f599", MappedType = typeof(FxTextureModes))]
    public readonly InputSlot<int> FxTextureMode = new();

    [Input(Guid = "d2e7c5ab-733a-4d9c-9be3-f4cadf5469e9")]
    public readonly InputSlot<Vector4> FxTextureAmount = new();

    [Input(Guid = "e9ff3d61-3d42-4591-8526-5ebf8c8c6806")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "ff7f8b1e-d883-4efb-bb9f-ce354793ff56")]
    public readonly InputSlot<float> AlphaCut = new();

    [Input(Guid = "c73469a1-cea6-4aa3-ad4b-e035bd172576")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "50336981-2e7e-46c2-bb9d-f4194d83cc95")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "60a4dc01-07bc-4778-9bbf-7e8824fd9e5c")]
    public readonly InputSlot<CullMode> CullMode = new();
        
    private enum Orientations
    {
        Billboard,
        RotatedBillboard,
        PointRotation,
    }
        
    private enum DistributionModes
    {
        RandomWithPhase,
        Scatter,
        Spread,
        UseW,
        UseFogDistance,
    }

    private enum ScaleModes
    {
        Multiply,
        Add,
        Override,
    }

    private enum FxTextureModes
    {
        UseColor,
        UseAs_RotateScaleScatter,
    }
}