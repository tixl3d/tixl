namespace Examples.user.pixtur.vj.scenes;

[Guid("fdaed904-b7be-4cf9-97ce-5c2090c6571e")]
public class VJFraeser : Instance<VJFraeser>
{
    [Output(Guid = "a20ad6f7-7546-4f5a-8cc7-3df472c841b2")]
    public readonly Slot<Command> Output = new Slot<Command>();


}