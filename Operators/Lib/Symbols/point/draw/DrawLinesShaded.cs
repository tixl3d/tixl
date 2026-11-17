using T3.Core.Utils;

namespace Lib.point.draw;

[Guid("90a5794c-4cec-41e1-bd79-403b40e5666f")]
internal sealed class DrawLinesShaded : Instance<DrawLinesShaded>
{
    [Output(Guid = "666fc224-b249-4959-b07f-4a6482344a1b")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "fe1ca570-d864-4a18-a500-d7b2311a9c78")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "66c56537-7036-48a5-866b-3d21dc756c0e")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "e4ae8056-00bb-4220-80fc-69457debb26c")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "3082feed-8084-4248-862f-f92c75323583", MappedType = typeof(WidthFXModes))]
    public readonly InputSlot<int> WidthFactor = new();

    [Input(Guid = "76790f65-315a-42ee-b509-d01475a79f49")]
    public readonly InputSlot<bool> UsePointScale = new();

    [Input(Guid = "c000197c-192e-4073-a5aa-f2570933c4c8")]
    public readonly InputSlot<int> Smoothing = new();

    [Input(Guid = "5a4e9d51-27c0-4c85-8cd9-37d7dbc27ce3")]
    public readonly InputSlot<bool> EnableZWrite = new();

    [Input(Guid = "0295f9fc-3cef-4e93-a3a5-844c7f1d06e9")]
    public readonly InputSlot<bool> EnableZTest = new();

    [Input(Guid = "f219a75c-a4ab-4e09-a5cb-ab7448101317", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "0b1996a0-2255-4289-a07d-7a423fafc191")]
    public readonly InputSlot<float> FadeNearest = new();

    [Input(Guid = "51a25054-678d-47f1-9de6-f7180515c178")]
    public readonly InputSlot<ShaderGraphNode> ColorField = new();

    private enum WidthFXModes
    {
        None = 0,
        F1 = 1,
        F2 = 2,
    }
}
