namespace Examples.user.pixtur.project.particleMap;

[Guid("3b902c86-e284-4a2b-969c-65e79a14ceba")]
public class TestParticleMap : Instance<TestParticleMap>
{
    [Output(Guid = "b4234870-9d72-4fef-8345-0e80cbf00801")]
    public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


}