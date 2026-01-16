namespace Skills.Image.I02_Gradients;

[Guid("0a591e7e-5631-4f32-9ea2-5fddbdb6566f")]
internal sealed class I02n_Polar : Instance<I02n_Polar>
{
    [Output(Guid = "d883f10f-a80d-490b-bf77-2de9f3a54e7d")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}