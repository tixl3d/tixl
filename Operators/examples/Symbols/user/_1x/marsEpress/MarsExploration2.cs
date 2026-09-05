namespace Examples.user._1x.marsEpress;

[Guid("ff101c21-e166-466e-8582-84858789f3b6")]
 internal sealed class MarsExploration2 : Instance<MarsExploration2>
{
    [Output(Guid = "021d44d2-96c0-4ea9-b41d-d37a703ba3fa")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}