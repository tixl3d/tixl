namespace Examples.user.pixtur.dailies;

[Guid("fe9ef18c-7780-42f4-bf25-d37b21ea7c52")]
public class HitMan2 : Instance<HitMan2>
{
    [Output(Guid = "fd5c5695-2898-4027-b0cb-a719c06f5257")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}