namespace Lib.mesh.draw;

[Guid("dcacc281-92c6-4e47-8eea-91fa8954ed86")]
internal sealed class DrawMeshCelShading : Instance<DrawMeshCelShading>
{
    [Output(Guid = "17c56856-5829-4e60-a359-809334a225d1")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "8fa2a214-8b1d-4182-b4c8-145c3e168f87")]
    public readonly InputSlot<System.Numerics.Vector4> Color = new();

    [Input(Guid = "e1e45d58-4aa1-4bf4-8e9d-3b64cf82fd89")]
    public readonly InputSlot<System.Numerics.Vector4> EdgeColor = new();

    [Input(Guid = "cff8d554-12b4-402f-8be9-98bba5ec406b")]
    public readonly InputSlot<float> Brightness = new();

    [Input(Guid = "a9a04304-2af3-49e8-a28e-bcac8361b17e")]
    public readonly InputSlot<float> EdgeThresold = new();

    [Input(Guid = "3d2eaff6-9b02-4bc9-9ebc-76f5888943f6")]
    public readonly InputSlot<T3.Core.DataTypes.Gradient> Shading = new();

    [Input(Guid = "8cda736b-a316-4188-9130-182cfe78b25b")]
    public readonly InputSlot<SharpDX.Direct3D11.CullMode> Culling = new();

    [Input(Guid = "44720f79-d2c5-4da7-98df-334539153eac")]
    public readonly InputSlot<bool> EnableZTest = new();

    [Input(Guid = "5455e5b1-d48a-45d5-bbb8-e205ea75c796")]
    public readonly InputSlot<bool> EnableZWrite = new();

    [Input(Guid = "aaca0bae-9943-45d5-8389-45343ad86e39")]
    public readonly InputSlot<Texture2D> ColorMap = new();

    [Input(Guid = "ada7f290-b23f-4b50-87b4-0e9b91c66fbf")]
    public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new();

}