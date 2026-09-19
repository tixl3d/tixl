namespace Examples.user.newemka980.Examples;

[Guid("9913adf3-8fe5-4d85-95ab-f04439c6edcb")]
internal sealed class BooleanExample : Instance<BooleanExample>
{
    [Output(Guid = "5b42af7d-3128-498e-8b96-cc707101e9e3")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}