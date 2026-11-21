namespace Examples.Lib.volume;

/// <summary>
/// Water sloshing in a shaking open box, built entirely from volume CSG and noise.
/// </summary>
[Guid("37449751-31a8-5ead-9525-88759fdfd1b0")]
internal sealed class VolumeShakingWaterExample : Instance<VolumeShakingWaterExample>
{
    [Output(Guid = "e29b2d3c-8e83-55d0-a789-f484a390fffc")]
    public readonly Slot<Texture2D> Output = new();
}
