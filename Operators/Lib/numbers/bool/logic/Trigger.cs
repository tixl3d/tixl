using T3.Core.Utils;

namespace Lib.numbers.@bool.logic;

[Guid("0bec016a-5e1b-467a-8273-368d4d6b9935")]
public sealed class Trigger : Instance<Trigger>
{
    [Output(Guid = "2451ea62-9915-4ec1-a65e-4d44a3758fa8")]
    public readonly Slot<bool> Result = new();

    public Trigger()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var value = BoolValue.GetValue(context);
        var wasHit = MathUtils.WasTriggered(value, ref _isSet);
        var onlyOnDown = OnlyOnDown.GetValue(context);

        Result.Value = onlyOnDown ? wasHit : value;

        var needsRefreshNextFrame = onlyOnDown && wasHit;
        Result.DirtyFlag.Trigger = needsRefreshNextFrame ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;

        ColorInGraph.DirtyFlag.Clear();
        Result.DirtyFlag.Clear();
    }

    private bool _isSet;
        
    [Input(Guid = "E7C1F0AF-DA6D-4E33-AC86-7DC96BFE7EB3")]
    public readonly InputSlot<bool> BoolValue = new();
        
    [Input(Guid = "6AD61E57-1073-483E-A0DD-96A9033AA39B")]
    public readonly InputSlot<bool> OnlyOnDown = new();
        
    [Input(Guid = "FA14AC1D-3247-4D36-BC96-14FF7356720A")]
    public readonly InputSlot<Vector4> ColorInGraph = new();
}