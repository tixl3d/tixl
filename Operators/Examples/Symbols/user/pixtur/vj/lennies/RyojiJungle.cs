namespace Examples.user.pixtur.vj.lennies;

[Guid("40a3acf4-4e8f-4728-9bc0-4e34a4c8cf8d")]
public class RyojiJungle : Instance<RyojiJungle>
{
    [Output(Guid = "7180f893-d902-4953-bb24-3864754e9e7a")]
    public readonly Slot<Texture2D> TextureOutput = new();


}