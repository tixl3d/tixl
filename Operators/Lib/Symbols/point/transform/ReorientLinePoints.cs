namespace Lib.point.transform;

[Guid("5dbe204c-ded0-4f23-bf6a-1de5cca21db6")]
internal sealed class ReorientLinePoints : Instance<ReorientLinePoints>
{

    [Output(Guid = "3c051df4-1edc-49dd-88f1-8db923bf0e3e")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "d0c4731f-d84d-492c-ad62-7b635ef83407")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "70c1746a-3cf1-4a20-829e-18fd846b9133")]
    public readonly InputSlot<float> Amount = new();

    [Input(Guid = "088a9588-551d-4166-8d9d-a313e6dca44e")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "c5cbca87-0f5e-4c0b-87b6-00e6404a180e")]
    public readonly InputSlot<Vector3> UpVector = new();

    [Input(Guid = "d90ad85c-2cdf-4eea-8499-63f2f9c91812")]
    public readonly InputSlot<bool> WIsWeight = new();

    [Input(Guid = "7e790989-90fd-44a6-b937-4665dad0d06f")]
    public readonly InputSlot<bool> Flip = new();
}