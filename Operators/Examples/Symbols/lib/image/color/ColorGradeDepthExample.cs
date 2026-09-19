namespace Examples.Lib.image.color;

[Guid("737a41db-bf35-4f66-8600-a083f0157cd5")]
 internal sealed class ColorGradeDepthExample : Instance<ColorGradeDepthExample>
{
    [Output(Guid = "383f44bf-888c-413e-bb64-5400b30cfb70")]
    public readonly Slot<Texture2D> Output = new();


}