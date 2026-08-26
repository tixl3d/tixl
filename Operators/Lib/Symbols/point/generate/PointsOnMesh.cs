namespace Lib.point.generate;

[Guid("17188f49-1243-4511-a46c-1804cae10768")]
internal sealed class PointsOnMesh : Instance<PointsOnMesh>
{

    [Output(Guid = "414724f2-1c7f-406a-a209-cfb3f6ad0265")]
    public readonly Slot<BufferWithViews> ResultPoints = new();

    [Output(Guid = "dedeebfb-07a9-4ab3-98fc-18d5dd6b1d33")]
    public readonly Slot<BufferWithViews> Colors = new();

    [Input(Guid = "3289dbe7-14f0-4bf0-b223-bb57419cf179")]
    public readonly InputSlot<MeshBuffers> Mesh = new InputSlot<MeshBuffers>();

    [Input(Guid = "b3d526ee-b1f1-4254-b702-1980d659e557")]
    public readonly InputSlot<int> Count = new InputSlot<int>();

    [Input(Guid = "d737860b-2864-472d-ad46-4061b63a12d4")]
    public readonly InputSlot<float> Seed = new InputSlot<float>();

    [Input(Guid = "132584c0-c27c-448a-b31d-ae72f0fb4baa")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "1843683c-53a2-4862-a9a5-4b3afe729ace")]
    public readonly InputSlot<bool> UseVertexSelection = new InputSlot<bool>();

    [Input(Guid = "ed758e72-6977-44e0-a997-bf0ad83f6ceb")]
    public readonly InputSlot<bool> IsEnabled = new InputSlot<bool>();
}