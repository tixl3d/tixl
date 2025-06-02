namespace Examples.Lib.image.use;

[Guid("1fdd634f-4c6a-4615-b75a-0c46732c9826")]
 internal sealed class UseRenderTargetExample : Instance<UseRenderTargetExample>
{
    [Output(Guid = "2f32cf47-be6e-4ac8-a2e5-6e967edb64b1")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}