namespace Lib.image.generate._obsolete;

[Guid("97eb35ec-2825-4f63-8fdf-3fe38fa9e652")]
internal sealed class _FractalNoiseOld : Instance<_FractalNoiseOld>
{
    [Output(Guid = "bfce8bf6-9ef3-4fdb-8c6e-21aa65485f14")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "6fe3a975-6bb5-4cf1-9a24-6cf04ac800b6")]
    public readonly InputSlot<Vector4> ColorA = new();

    [Input(Guid = "a966d9a1-b36f-4f85-b918-8651ea7d055d")]
    public readonly InputSlot<Vector4> ColorB = new();

    [Input(Guid = "06241d83-1ff8-460a-b363-db3204384838")]
    public readonly InputSlot<Vector2> Offset = new();

    [Input(Guid = "2381e8e4-a4e5-4691-9059-3b2096146970")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "59bde7a3-0952-46a8-8285-40f0a00488e6")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "4de046cc-26d7-466f-90b4-3a3bdd59acb3")]
    public readonly InputSlot<float> Phase = new();

    [Input(Guid = "0e3d1c9f-d0aa-4765-a0ab-d8292a981368")]
    public readonly InputSlot<float> Bias = new();

    [Input(Guid = "37b24ceb-2d25-43e8-a31c-7caf62c2b7ee")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "120f0ce7-8560-477d-8e71-5bcbeae932c0")]
    public readonly InputSlot<Vector3> WarpOffset = new();

    [Input(Guid = "6ae1817e-eeeb-4bc9-802f-00fc26303023")]
    public readonly InputSlot<int> Iterations = new();

    [Input(Guid = "270f9603-1d9b-412b-9c76-330eafc8a958", MappedType = typeof(Methods))]
    public readonly InputSlot<int> Method = new();

    [Input(Guid = "0512491d-3022-48c2-b79d-4688ff18fd0e")]
    public readonly InputSlot<bool> GenerateMips = new();


    private enum Methods
    {
        Legacy,
        OpenSimplex2S,
        OpenSimplex2S_NormalMap,
    }
}