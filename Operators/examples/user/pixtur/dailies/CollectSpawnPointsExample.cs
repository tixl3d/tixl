namespace Examples.user.pixtur.dailies;

[Guid("1c99e732-7fd7-4254-8ab2-3a9cf2325982")]
public class CollectSpawnPointsExample : Instance<CollectSpawnPointsExample>
{
    [Output(Guid = "eb14540d-5d13-4b83-a2ed-32535a399815")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}