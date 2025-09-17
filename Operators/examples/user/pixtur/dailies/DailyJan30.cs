namespace Examples.user.pixtur.dailies;

[Guid("7dd56d5d-56a6-49dd-9bd5-bb4a1ee78cbe")]
public class DailyJan30 : Instance<DailyJan30>
{
    [Output(Guid = "464634f2-8e29-47f5-9260-997164c1dbb5")]
    public readonly Slot<Texture2D> TextureOutput = new();


}