namespace Examples.user.newemka980.visuals;

[Guid("40a73341-0210-4d77-b893-b57dfd3d9d90")]
internal sealed class SphereFlower : Instance<SphereFlower>
{
    [Output(Guid = "0527ab8f-e0a6-4630-a4dc-61cf41a47581")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


}