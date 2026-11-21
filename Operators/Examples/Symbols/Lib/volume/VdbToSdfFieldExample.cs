namespace Examples.Lib.volume;

/// <summary>
/// A volume inside the SDF world: a torus volume melts into a wandering SDF sphere.
/// </summary>
[Guid("12016fb4-ffa4-5655-81e9-aa833e9fddcb")]
internal sealed class VdbToSdfFieldExample : Instance<VdbToSdfFieldExample>
{
    [Output(Guid = "974939ea-9ec1-5c6e-9957-18cf961bbb59")]
    public readonly Slot<Texture2D> Output = new();
}
