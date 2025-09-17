namespace Examples.user.pixtur.vj;

[Guid("6c39dca3-8d0c-477f-923b-30f52f2b4efa")]
public class LineCondomExperiment : Instance<LineCondomExperiment>
{
    [Output(Guid = "87ebe078-e42b-4b65-a876-09a4f49a8530")]
    public readonly Slot<Command> Detection = new();


}