namespace Examples.benchmarks;

[Guid("061cffbf-ba0e-515e-8342-0f940cde0c53")]
internal sealed class BenchFillrate : Instance<BenchFillrate>
{
    [Output(Guid = "39851ff3-0b82-54e4-a30f-7173e266face")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "a8af9219-f2e3-5b58-bc15-2a78e95797ac")]
    public readonly InputSlot<int> Load = new();
}
