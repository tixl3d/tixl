namespace Examples.Lib.point;

[Guid("0b019a98-0470-4d98-9d34-e06abd8c72d1")]
 internal sealed class SoftTransformPointsExample : Instance<SoftTransformPointsExample>
{
    [Output(Guid = "d4ccbf12-6e9e-461e-867b-bc72b89afc80")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}