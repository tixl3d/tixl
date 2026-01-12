namespace Skills.Image.I02_Gradients;

[Guid("e207e194-3f2d-48b6-b195-d1e147b09cca")]
internal sealed class I02j_ChainOverlay : Instance<I02j_ChainOverlay>
{
    [Output(Guid = "d8a6033e-b69c-482c-b637-e9cde4a013a5")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}