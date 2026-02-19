using T3.Core.Utils;

namespace Lib.render.basic;

[Guid("ff37499b-2550-4795-83fd-626ba6d2f2fb")]
internal sealed class DrawScreenQuadAdvanced :Instance<DrawScreenQuadAdvanced>{
    [Output(Guid = "e7d7d91a-2616-4fe7-ba89-02bf4288458c")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "5dc5ef82-ec67-4d30-acfc-0d05fd1e66b9")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "0a7c5acd-ae50-47a7-98e5-432b658a8bbd")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "467410b1-5fc8-4448-8eb4-6383f1c98bf4")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "52ba1d58-ce0d-432c-b131-6db1ff0277d9")]
    public readonly InputSlot<float> Height = new();

    [Input(Guid = "cb98de8e-8896-4314-a8d3-87580d91d5d4", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "8de08b8e-8230-4126-8aba-89125ed559c0")]
    public readonly InputSlot<bool> EnableDepthTest = new();

    [Input(Guid = "3bc5a88f-1102-4e0d-84d8-da6c206886e6")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "5a2e54a4-eaab-4ebc-928f-44f0373d5f39")]
    public readonly InputSlot<Vector2> Position = new();

    [Input(Guid = "0b216554-21b6-433b-88c4-541d7030ba3e")]
    public readonly InputSlot<Filter> Filter = new();

        [Input(Guid = "b5e29ca1-4431-40b9-9881-aa03d008dee2")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> DepthBuffer = new InputSlot<T3.Core.DataTypes.Texture2D>();
}