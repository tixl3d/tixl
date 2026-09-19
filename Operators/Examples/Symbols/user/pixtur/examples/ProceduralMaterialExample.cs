namespace Examples.user.pixtur.examples;

[Guid("8fc90e3e-169b-41ba-a76a-51e74f183eb4")]
 internal sealed class ProceduralMaterialExample : Instance<ProceduralMaterialExample>
{
    [Output(Guid = "00a05165-eb5e-4643-812c-3363fa5bd0f2")]
    public readonly Slot<Texture2D> ImgOutput = new Slot<Texture2D>();


}