namespace Examples.user.still.worksforeverybody.elements;

[Guid("b60f2321-0df5-42a7-bdcf-4660bbe549d6")]
internal sealed class WipOverlay : Instance<WipOverlay>
{
    [Output(Guid = "434a3d5c-96c3-4f5e-962e-b88a4415fce3")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "2095ce9d-2b92-45cb-bf30-91c32d8ebd90")]
    public readonly InputSlot<string> InputString = new();


}