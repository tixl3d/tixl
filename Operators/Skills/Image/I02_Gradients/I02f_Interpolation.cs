namespace Skills.Image.I02_Gradients;

[Guid("88adf4e5-64af-4a86-b8f6-2b236fa7e4f3")]
internal sealed class I02f_Interpolation : Instance<I02f_Interpolation>
{
    [Output(Guid = "ebc6faf3-a36c-4a14-9598-b37ada4b4b69")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}