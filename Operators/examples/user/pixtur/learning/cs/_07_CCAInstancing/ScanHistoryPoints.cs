namespace Examples.user.pixtur.learning.cs._07_CCAInstancing;

[Guid("23ff34cd-7db7-4999-a0d1-bc3dfdb1f863")]
public class ScanHistoryPoints : Instance<ScanHistoryPoints>
{
    [Output(Guid = "6f470a63-d210-430e-b53a-667be5e2c180")]
    public readonly Slot<BufferWithViews> Output2 = new();

    [Input(Guid = "7f43a23b-8570-456d-8b9d-35317762b545")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "bc88740f-b685-4b45-afb1-107d8a566c57")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "460f8203-3272-4b06-a96a-75199f51f1bb")]
    public readonly InputSlot<float> LoopDuration = new();

    [Input(Guid = "520439e2-a477-4114-8f53-4a7e73ccf718")]
    public readonly InputSlot<float> Spacing = new();

}