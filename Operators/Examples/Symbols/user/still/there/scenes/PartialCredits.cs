namespace Examples.user.still.there.scenes;

[Guid("ec9d9b04-9f86-49f9-9463-f5ba04e4ee00")]
internal sealed class PartialCredits : Instance<PartialCredits>
{

    [Output(Guid = "318f3a61-5bbb-4f57-9b11-2d7418e6ff43")]
    public readonly TimeClipSlot<Command> Output2 = new();


}