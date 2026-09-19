namespace Examples.user.pixtur.vj;

[Guid("a9bd30b1-8b58-4b88-8c8e-5f14b425edec")]
public class VJSpermZoomer : Instance<VJSpermZoomer>
{
    [Output(Guid = "e747675e-ca85-471c-9661-baae32112caa")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "42d2618e-e968-441e-8fb7-453d4e326509")]
    public readonly InputSlot<Command> Scene = new();


}