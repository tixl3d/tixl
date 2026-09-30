namespace Examples.Lib.point;

[Guid("01ebb7a3-9caa-4259-aaa1-c79248b39325")]
 internal sealed class SamplePointAttributesExample2 : Instance<SamplePointAttributesExample2>
{
    [Output(Guid = "acf384c2-66d8-4802-ab79-de2ac9eef9a4")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}