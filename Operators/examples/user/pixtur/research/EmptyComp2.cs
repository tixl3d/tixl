namespace Examples.user.pixtur.research;

[Guid("117c9c2d-835f-4460-bf53-2f68226ae8ee")]
public class EmptyComp2 : Instance<EmptyComp2>
{
    [Output(Guid = "863660ad-3ae8-4714-9584-34eaae3a8e9d")]
    public readonly Slot<float> Result = new();


}