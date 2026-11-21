namespace Examples.Lib.volume;

/// <summary>
/// Organic shapes: a sphere displaced by noise, then grown and smoothed, step by step.
/// </summary>
[Guid("748fdcc7-6b02-5fdc-8eac-94039edb9124")]
internal sealed class VolumeNoiseExample : Instance<VolumeNoiseExample>
{
    [Output(Guid = "9361eb81-eb0c-536e-87e8-6db9e14ef3bb")]
    public readonly Slot<Texture2D> Output = new();
}
