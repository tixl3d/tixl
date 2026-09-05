namespace Examples.user.pixtur.vj;

[Guid("e14af8a3-8672-4348-af9e-735714c31c92")]
public class SetVJVariables : Instance<SetVJVariables>
{

    [Output(Guid = "a8127182-4b8d-4be2-8c50-9ce475d2699d")]
    public readonly Slot<Texture2D> Output2 = new();

        
    [Input(Guid = "693345bd-0cd8-4dca-9416-42a9bdcbc293")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "83188b7c-4bb7-4884-b6f0-d73fbf7debe1", MappedType = typeof(Devices))]
    public readonly InputSlot<int> MidiDevice = new InputSlot<int>();

    private enum Devices
    {
        ApcMini,
        Apc40MKII,
        MidiMix,
    }
}