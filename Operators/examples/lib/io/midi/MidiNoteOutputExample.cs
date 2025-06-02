namespace Examples.Lib.io.midi;

[Guid("cb2cdd1d-811a-4a30-a5bc-289f36c31028")]
 internal sealed class MidiNoteOutputExample : Instance<MidiNoteOutputExample>
{
    [Output(Guid = "3a972c37-fcf8-4c5f-b932-97ac9f6d5d99")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}