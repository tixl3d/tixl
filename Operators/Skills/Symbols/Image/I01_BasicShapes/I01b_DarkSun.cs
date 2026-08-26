namespace Skills.Image.I01_BasicShapes;

[Guid("40f7a388-3125-458c-9b57-e2daeea60b9f")]
internal sealed class I01b_DarkSun : Instance<I01b_DarkSun>
{
    [Output(Guid = "986bb070-7100-497d-a6be-d1f174d3c7f1")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}