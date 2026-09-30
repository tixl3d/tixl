namespace Examples.Lib.flow.context;

[Guid("cce36a29-8f66-492d-bf8f-b924fa1ae384")]
 internal sealed class SetContextVariableExample : Instance<SetContextVariableExample>
{
    [Output(Guid = "30ec9e5d-97af-4621-9e20-ef651bfd2ec3")]
    public readonly Slot<Command> Output = new();


}