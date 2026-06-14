#nullable enable
using T3.Core.IO;
using T3.Core.Utils;

namespace Lib.io.input;

[Guid("7b4e9a30-2c81-4d6f-9a15-3e8f1c2b4d70")]
internal sealed class ActivateSnapshot : Instance<ActivateSnapshot>
{
    [Output(Guid = "8c5f0a41-3d92-4e7a-ab26-4f9a2d3c5e81")]
    public readonly Slot<Command> Result = new();

    public ActivateSnapshot()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var index = Index.GetValue(context);

        // Fire once on the rising edge of SetTrigger; the editor wraps the index over the snapshot count.
        if (MathUtils.WasTriggered(SetTrigger.GetValue(context), ref _wasTriggered) && Parent != null)
            SnapShotBlendingData.PendingActivationsByComposition[Parent.Symbol.Id] = index;
    }

    [Input(Guid = "9d60fb52-4ea3-4f8b-bc37-5a0b3e4d6f92")]
    public readonly InputSlot<int> Index = new();

    [Input(Guid = "ae71ac63-5fb4-4a9c-cd48-6b1c4f5e70a3")]
    public readonly InputSlot<bool> SetTrigger = new();

    private bool _wasTriggered;
}
