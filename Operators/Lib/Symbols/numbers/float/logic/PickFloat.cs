using Lib.Utils;

namespace Lib.numbers.@float.logic;

[Guid("63e6e642-827b-4518-ac64-9ab0a8d4391e")]
internal sealed class PickFloat : Instance<PickFloat>
{
    [Output(Guid = "72ADD436-84AA-4332-B061-BE8D50981C77")]
    public readonly Slot<float> Selected = new();

    public PickFloat()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (PickedInput.TryGetValue(FloatValues, Index.GetValue(context), context, out var value))
            Selected.Value = value;
    }

    [Input(Guid = "D7EF7F1A-A6BD-4F94-A29A-BB19E2854001")]
    public readonly MultiInputSlot<float> FloatValues = new();

    [Input(Guid = "465B4FC3-899C-4B97-9892-F237FA6613E8")]
    public readonly InputSlot<int> Index = new(0);
}