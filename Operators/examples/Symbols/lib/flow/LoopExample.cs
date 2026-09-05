namespace Examples.Lib.flow;

[Guid("a3945dbe-d5be-4f0f-a904-8ee287d14a9f")]
 internal sealed class LoopExample : Instance<LoopExample>
{
    [Output(Guid = "3cdfc9d7-bf03-4481-9869-ae8f43187304")]
    public readonly Slot<Command> Output = new();


}