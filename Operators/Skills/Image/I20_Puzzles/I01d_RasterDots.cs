namespace Skills.Image.I20_Puzzles;

[Guid("a91e8394-db72-4fcb-8cd6-0676ada49848")]
internal sealed class I01d_RasterDots : Instance<I01d_RasterDots>
{
    [Output(Guid = "9a01e522-0bbd-4190-89ac-b9dba2519026")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}