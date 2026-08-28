using T3.Core.Utils;

namespace Lib.image.generate.basic;

[Guid("dc2273a7-8a54-4e6f-8d8e-9a675c1ef599")]
internal sealed class BoxGradient : Instance<BoxGradient>
{
    [Output(Guid = "04ffa9f0-346a-4dd7-83fb-af13cae73722")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "71cd9153-da17-47f0-9251-80bdef8906b3")]
    public readonly InputSlot<Texture2D> Image = new ();

    [Input(Guid = "1dd6cf1e-d374-40d5-b51f-21b91deb3802")]
    public readonly InputSlot<float> Rotation = new InputSlot<float>();

    [Input(Guid = "e155a2f2-2b21-49b5-9bd4-0c363684d93f")]
    public readonly InputSlot<Vector2> Center = new InputSlot<Vector2>();

    [Input(Guid = "06f28039-d349-4f5f-a68f-6009886e180b")]
    public readonly InputSlot<Vector2> Size = new InputSlot<Vector2>();

    [Input(Guid = "5a164383-4f9d-4978-bfcb-a1c48b9b8f34")]
    public readonly InputSlot<float> UniformScale = new InputSlot<float>();

    [Input(Guid = "06553d30-0a71-435b-8796-b61db670fbc6")]
    public readonly InputSlot<Vector4> CornersRadius = new InputSlot<Vector4>();

    [Input(Guid = "5e7cd523-0c39-42e4-a4e9-05cc20477296")]
    public readonly InputSlot<Gradient> Gradient = new InputSlot<Gradient>();

    [Input(Guid = "94d46ad8-7dd6-490c-ade4-a527c7ee9d05")]
    public readonly InputSlot<float> GradientWidth = new InputSlot<float>();

    [Input(Guid = "c3d48bd0-5153-4631-ae6c-a7ded46ce952")]
    public readonly InputSlot<float> Offset = new InputSlot<float>();

    [Input(Guid = "c9e1ea04-1c81-4554-964f-e361a502b9b8")]
    public readonly InputSlot<bool> PingPong = new InputSlot<bool>();

    [Input(Guid = "8f50765e-3d85-4814-a99a-ecdff18c97e7")]
    public readonly InputSlot<bool> Repeat = new InputSlot<bool>();

    [Input(Guid = "94516412-20a6-41a9-a036-a3b5ed67b04a")]
    public readonly InputSlot<Vector2> GainAndBias = new InputSlot<Vector2>();

    [Input(Guid = "63241bae-bfe6-415f-9288-ffcf1be15fc6", MappedType = typeof(SharedEnums.RgbBlendModes))]
    public readonly InputSlot<int> BlendMode = new InputSlot<int>();

    [Input(Guid = "70641015-e77b-4a75-b3f2-eb3534cceead")]
    public readonly InputSlot<Int2> Resolution = new InputSlot<Int2>();
}