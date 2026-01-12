namespace Skills.Basics.B01_Welcome;

[Guid("bd3f2fa7-48f7-4ca3-8c32-92fe0d9e3fff")]
internal sealed class B01b_RearrangeStacks : Instance<B01b_RearrangeStacks>
{
    [Output(Guid = "e4c0801a-99f8-442c-888d-d207fc36dace")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}