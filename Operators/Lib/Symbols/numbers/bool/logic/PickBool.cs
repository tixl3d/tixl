using Lib.Utils;

namespace Lib.numbers.@bool.logic;

[Guid("b98ab7bd-1843-4208-9bdb-c279dfc5b5aa")]
internal sealed class PickBool : Instance<PickBool>
{
    [Output(Guid = "70F3F63E-5623-4D54-BD8A-3EF3A39F8D51")]
    public readonly Slot<bool> Selected = new();

    public PickBool()
    {
        Selected.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        if (PickedInput.TryGetValue(BoolValues, Index.GetValue(context), context, out var value))
            Selected.Value = value;
    }

    [Input(Guid = "1F97F10C-3158-446B-85EC-8DFDB25B4D67")]
    public readonly MultiInputSlot<bool> BoolValues = new();

    [Input(Guid = "ef785e90-49de-4075-be85-09327d48cb16")]
    public readonly InputSlot<int> Index = new(0);
}