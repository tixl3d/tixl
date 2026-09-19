namespace Examples.user.pixtur.vj;

[Guid("aaf19aea-06f6-4c5b-8765-9910f8ed7ad0")]
public class SyncedRandomScroller : Instance<SyncedRandomScroller>
{
    [Output(Guid = "27b856b4-11dc-40af-8823-ed43e69d447f")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "b6e11e35-ea50-493c-9be1-7d328f54807c")]
    public readonly InputSlot<bool> RandomTrigger = new();

    [Input(Guid = "69795161-4890-495b-9298-3ef6c0a1cd81")]
    public readonly InputSlot<bool> RandomTriggerB = new();

    [Input(Guid = "f59234de-a3a7-43b7-9fce-05716515e278")]
    public readonly InputSlot<int> MaxLength = new();


}