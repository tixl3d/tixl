namespace Examples.Lib.point.io;

[Guid("7a25f962-a800-49a0-9394-cf03f6742661")]
internal sealed class CsvToPointsExample : Instance<CsvToPointsExample>
{
    [Output(Guid = "d210eb56-2e3f-4889-bbfe-1864d8c73bcb")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "2e752c92-5a39-4d39-ba43-a8ab2be81c6d")]
    public readonly InputSlot<string> CsvFile = new();
}
