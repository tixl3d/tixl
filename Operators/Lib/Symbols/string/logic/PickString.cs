using Lib.Utils;

namespace Lib.@string.logic;

[Guid("a9784e5e-7696-49a0-bb77-2302587ede59")]
internal sealed class PickString : Instance<PickString>
{
    [Output(Guid = "74104EB6-DFC2-4AD2-9600-91C5A33855D4")]
    public readonly Slot<string> Selected = new();

    public PickString()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (PickedInput.TryGetValue(Input, Index.GetValue(context), context, out var value))
            Selected.Value = value;
    }

    [Input(Guid = "202CE6D5-EE5A-41C7-BD04-4C1490F3EA9C")]
    public readonly MultiInputSlot<string> Input = new();

    [Input(Guid = "20E76577-92EE-443D-9630-EBC41E38BB85")]
    public readonly InputSlot<int> Index = new(0);
}