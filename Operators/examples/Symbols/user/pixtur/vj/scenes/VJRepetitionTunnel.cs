namespace Examples.user.pixtur.vj.scenes;

[Guid("eb863321-07e8-41d3-ae6e-29d9abacfa66")]
public class VJRepetitionTunnel : Instance<VJRepetitionTunnel>
{
    [Output(Guid = "3d5621f4-ca45-4bd9-8bcf-5205209ea080")]
    public readonly Slot<Command> Output = new Slot<Command>();


}