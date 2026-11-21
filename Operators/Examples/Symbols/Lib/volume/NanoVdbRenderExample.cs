namespace Examples.Lib.volume;

/// <summary>
/// Liquid, smoke and fire raymarched from NanoVDB grids in one glass box.
/// </summary>
[Guid("792104ff-1cda-5a84-93dd-463e60448572")]
internal sealed class NanoVdbRenderExample : Instance<NanoVdbRenderExample>
{
    [Output(Guid = "fb7a479b-1899-5e7a-91dc-3f5d15f89488")]
    public readonly Slot<Texture2D> Output = new();
}
