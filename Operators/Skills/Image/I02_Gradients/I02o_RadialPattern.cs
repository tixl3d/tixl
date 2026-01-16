namespace Skills.Image.I02_Gradients;

[Guid("58c70018-0764-43c3-8bc5-c7257c6dc5ba")]
internal sealed class I02o_RadialPattern : Instance<I02o_RadialPattern>
{
    [Output(Guid = "f6862db7-525f-4b2d-82f8-ae558605ab76")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}