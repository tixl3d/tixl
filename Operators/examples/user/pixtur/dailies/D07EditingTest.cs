namespace Examples.user.pixtur.dailies;

[Guid("05f5733d-0aa1-462e-a02f-f8c639f07152")]
public class D07EditingTest : Instance<D07EditingTest>
{
    [Output(Guid = "1e330a2e-a9b4-4c6d-8509-20f9bd13d40d")]
    public readonly Slot<Texture2D> TextureOutput = new();


}