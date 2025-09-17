namespace Examples.user.pixtur.research;

[Guid("ffccf37c-d104-4c25-a626-460e52677083")]
public class EmptyComp : Instance<EmptyComp>
{
    [Output(Guid = "e287f489-cc3a-4c4b-81e6-7dd0b5e078d5")]
    public readonly Slot<float> Result = new();


}