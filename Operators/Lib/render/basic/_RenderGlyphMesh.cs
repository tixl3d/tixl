using T3.Core.DataTypes;

namespace Lib.render.basic;

[Guid("D5A6B7C8-9D0E-4F10-A1B2-C3D4E5F6A7B8")]
internal sealed class _RenderGlyphMesh : Instance<_RenderGlyphMesh>
{
    [Output(Guid = "2B3C4D5E-6F70-4A80-B9C0-D1E2F3A4B5C6")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "E0F1A2B3-C4D5-4E6F-A7B8-C9D0E1F2A3B4")]
    public readonly InputSlot<Vector4> Color = new InputSlot<Vector4>();

    [Input(Guid = "A4B5C6D7-E8F9-4A0B-1C2D-3E4F5A6B7C8D")]
    public readonly InputSlot<Vector4> Shadow = new InputSlot<Vector4>();

    [Input(Guid = "62827185-635E-4BF4-B13A-301547B55ACC")]
    public readonly InputSlot<float> Sharpness = new InputSlot<float>();

    [Input(Guid = "A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D")]
    public readonly InputSlot<string> FontPath = new InputSlot<string>();

    [Input(Guid = "B1C2D3E4-F5A6-4B7C-8D9E-0F1A2B3C4D5E")]
    public readonly InputSlot<Buffer> Buffer = new InputSlot<Buffer>();

    [Input(Guid = "C1D2E3F4-A5B6-4C7D-8E9F-0A1B2C3D4E5F")]
    public readonly InputSlot<int> VertexCount = new InputSlot<int>();

    [Input(Guid = "D1E2F3A4-B5C6-4D7E-8F9A-0B1C2D3E4F5A")]
    public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

    [Input(Guid = "E2F3A4B5-C6D7-4E8F-9A0B-1C2D3E4F5A6B")]
    public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

    [Input(Guid = "F3A4B5C6-D7E8-4F9A-0B1C-2D3E4F5A6B7C")]
    public readonly InputSlot<bool> BillboardMode = new InputSlot<bool>();
}
