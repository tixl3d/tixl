namespace Examples.user.pixtur.vj;

[Guid("3664007f-9105-40b7-ba58-1466e4f6edbd")]
public class Deadline2021 : Instance<Deadline2021>
{
    [Output(Guid = "f1e5bb3f-8f4f-4fe7-b47d-2195fb19fee8")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}