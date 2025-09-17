namespace Examples.user.pixtur.examples;

[Guid("a8f108a4-4d0a-4335-a3b5-f723239c25d1")]
public class VennDiagram : Instance<VennDiagram>
{
    [Output(Guid = "909e27e9-6b36-4f75-88c4-168f62c3a23a")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "c4019870-2fa0-4ddc-b30f-a5de66d4980d")]
    public readonly InputSlot<int> ShowIndex = new();

    [Input(Guid = "b5fae52e-45eb-4d15-a5ca-d14c222741c1")]
    public readonly InputSlot<string> Input = new();

    [Input(Guid = "ff6eec00-c597-45d7-8868-beaf2ec2cd5b")]
    public readonly InputSlot<string> Input3 = new();

    [Input(Guid = "64577bef-d074-4439-b797-85ca5519bab5")]
    public readonly InputSlot<string> Input2 = new();

}