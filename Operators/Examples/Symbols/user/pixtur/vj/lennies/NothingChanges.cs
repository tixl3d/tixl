namespace Examples.user.pixtur.vj.lennies;

[Guid("ee425946-3c6f-4bac-ab25-0e30571e8ca6")]
public class NothingChanges : Instance<NothingChanges>
{

    [Output(Guid = "24572cbd-ce42-4d53-9ff9-6780eb30e947")]
    public readonly Slot<Texture2D> Output2 = new();

    [Input(Guid = "3ed34802-c8ca-4182-920e-aa476a256430")]
    public readonly InputSlot<Command> MoreContent = new();


}