namespace Examples.Lib.io.midi;

[Guid("d53adae7-b5f0-4157-9710-4683a44d5601")]
internal sealed class APC40Mk1FeedbackExample : Instance<APC40Mk1FeedbackExample>
{
    [Output(Guid = "a466ccac-6f5d-4f0c-9976-4cfe2daf73fc")]
    public readonly Slot<Command> Output = new();
}
