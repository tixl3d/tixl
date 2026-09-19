namespace Examples.user.newemka980.Examples;

[Guid("f4528476-298d-42b7-8138-3916bea2da6e")]
internal sealed class SamplePointColorAttributesExample : Instance<SamplePointColorAttributesExample>
{
    [Output(Guid = "7bd91d7a-6b16-4837-b43c-f3f99b66ac86")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


}