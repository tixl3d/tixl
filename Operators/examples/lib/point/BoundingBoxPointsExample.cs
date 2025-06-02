namespace Examples.Lib.point;

[Guid("7bd3aab4-009f-4c5e-95c9-88f3d08b6893")]
 internal sealed class BoundingBoxPointsExample : Instance<BoundingBoxPointsExample>
{
    [Output(Guid = "3edf7f96-e0ac-4370-a099-fcfe30fee2dc")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


}