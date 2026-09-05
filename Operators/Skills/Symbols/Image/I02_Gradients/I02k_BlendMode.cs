namespace Skills.Image.I02_Gradients;

[Guid("cf84bdd7-14aa-4b4c-b461-cda2f525c696")]
internal sealed class I02k_BlendMode : Instance<I02k_BlendMode>
{
    [Output(Guid = "2d1c3890-e9f3-4656-b178-6bfb0672c2b3")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}