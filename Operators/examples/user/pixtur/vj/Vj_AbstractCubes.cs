namespace Examples.user.pixtur.vj;

[Guid("70e3725d-42d9-450a-8068-58ef244d0e09")]
public class Vj_AbstractCubes : Instance<Vj_AbstractCubes>
{
    [Output(Guid = "a6b68b2b-50a9-489a-a325-e0323a37b688")]
    public readonly Slot<Texture2D> TextureOutput = new();


}