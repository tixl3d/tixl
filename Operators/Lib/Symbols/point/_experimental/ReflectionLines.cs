namespace Lib.point._experimental;

[Guid("b5515341-24ef-48ff-b832-d40e8189c6a4")]
internal sealed class ReflectionLines : Instance<ReflectionLines>
{

    [Output(Guid = "d4437c90-9a13-4f35-a83f-b27dde3c4681")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "57917b87-9aa4-416b-8417-fc2ac9e849b0")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "517fa007-bf56-497f-be87-2574ff9125c6")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "25c7cc14-9422-4f47-b996-0e7d4ff0de69")]
    public readonly InputSlot<int> StepCount = new();

    [Input(Guid = "79d4a70d-c427-44fc-b917-646d71cd9647")]
    public readonly InputSlot<float> DecayW = new();

    [Input(Guid = "0af5f073-eb2b-4654-af69-2de7edc526e1")]
    public readonly InputSlot<float> ExtendSteps = new();

    [Input(Guid = "8a438ea7-b402-49a4-8783-1f721e494ee9")]
    public readonly InputSlot<float> SpreadColor = new();

    [Input(Guid = "877d9d58-a1f3-4b73-9379-7193091c082b")]
    public readonly InputSlot<float> SpreadColorShift = new();
}