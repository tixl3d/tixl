namespace Skills.Image.I02_Gradients;

[Guid("680bb1ae-d906-48cc-adbd-240e0df0497d")]
internal sealed class I02i_Flip : Instance<I02i_Flip>
{
    [Output(Guid = "e4ccc1b5-e160-43e9-84c3-990ae64c4fa1")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}