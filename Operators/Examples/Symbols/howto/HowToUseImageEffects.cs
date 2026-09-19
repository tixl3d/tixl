namespace Examples.howto;

[Guid("649d4542-4165-4129-9484-dd8fb8663e15")]
internal sealed class HowToUseImageEffects : Instance<HowToUseImageEffects>
{
    [Output(Guid = "30c39c3d-96cd-40ed-ae05-8b2a3eb6f177")]
    public readonly Slot<Texture2D> Texture = new();


}