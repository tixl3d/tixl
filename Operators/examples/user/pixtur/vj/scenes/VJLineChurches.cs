namespace Examples.user.pixtur.vj.scenes;

[Guid("60b237ce-5096-4aa5-bc56-76ae8d8016cd")]
public class VJLineChurches : Instance<VJLineChurches>
{
    [Output(Guid = "fe77ba13-276d-424a-87a2-c18f9154d312")]
    public readonly Slot<Command> Output = new Slot<Command>();


}