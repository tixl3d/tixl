namespace Examples.Lib.point;

[Guid("79a791ce-2490-4daa-a2a7-b4c024ecd735")]
 internal sealed class RepetitionPointsExample : Instance<RepetitionPointsExample>
{
    [Output(Guid = "8f3ff5da-fdda-439a-b1a0-28d3dc4f8722")]
    public readonly Slot<Texture2D> ImgOutput = new();


}