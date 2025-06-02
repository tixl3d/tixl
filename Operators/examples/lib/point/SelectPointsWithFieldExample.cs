namespace Examples.Lib.point;

[Guid("99cf2542-d408-4a81-ba49-4204eb2f6df8")]
internal sealed class SelectPointsWithFieldExample : Instance<SelectPointsWithFieldExample>
{
    [Output(Guid = "ac4e8f81-027e-4916-aeaa-9a3d13a62c18")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


}