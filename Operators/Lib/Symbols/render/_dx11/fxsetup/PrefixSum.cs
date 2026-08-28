namespace Lib.render._dx11.fxsetup;

[Guid("d35a403b-7e6e-4725-a344-6e8008a4e1e1")]
internal sealed class PrefixSum : Instance<PrefixSum>
{
    [Output(Guid = "a0801b0a-3447-4179-aa12-8b4b088868d2")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "faeb2a7e-de0f-4497-964b-7b21dd56f525")]
    public readonly Slot<BufferWithViews> ResultBuffer = new();

    [Input(Guid = "c5561f3b-495e-47e1-95d4-ea3a750f1842")]
    public readonly InputSlot<BufferWithViews> InputList2 = new();

    [Input(Guid = "c58270bf-a121-4bb0-9309-33bab48eb0a7")]
    public readonly InputSlot<bool> IsInclusive = new InputSlot<bool>();

}