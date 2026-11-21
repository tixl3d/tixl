namespace Examples.Lib.volume;

/// <summary>
/// Constructive solid geometry with volumes: a rounded box intersected with a sphere, then drilled through.
/// </summary>
[Guid("379fc9b9-e255-5630-9254-3fad8b5cb8fa")]
internal sealed class VolumeCsgExample : Instance<VolumeCsgExample>
{
    [Output(Guid = "b5c7f13e-5e01-5753-9fea-027da689e7fd")]
    public readonly Slot<Texture2D> Output = new();
}
