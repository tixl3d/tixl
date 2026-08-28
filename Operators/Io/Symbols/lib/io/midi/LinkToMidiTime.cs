namespace Lib.io.midi;

[Guid("3f10f526-d8ca-4f67-95a4-d703b713088e")]
internal sealed class LinkToMidiTime : Instance<LinkToMidiTime>
{
    [Output(Guid = "c9e7a901-caa7-4eba-bacf-9eaea2fa85cb")]
    public readonly Slot<Command> Commands = new();


    [Input(Guid = "57e9c133-e213-43d8-b0ed-e73e9b046a18")]
    public readonly InputSlot<Command> SubGraph = new();

    [Input(Guid = "5969cc2b-654e-490e-ab66-1511483f50ae")]
    public readonly InputSlot<bool> ResyncTrigger = new();

}