using Lib.Utils;

namespace Lib.data.@object;

[Guid("02fe1b32-8819-4c8a-8ab4-d353ebcddfe3")]
internal sealed class PickObject :Instance<PickObject>{
    
    [Output(Guid = "3AF9E7BC-A12C-465D-973D-5D3A8EF76B30")]
    public readonly Slot<object> Selected = new();

    public PickObject()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (PickedInput.TryGetValue(Input, Index.GetValue(context), context, out var value))
            Selected.Value = value;
    }
    
    [Input(Guid = "A4095B7E-2FAD-42E5-B518-848F2645AA7D")]
    public readonly MultiInputSlot<object> Input = new();

    [Input(Guid = "1fd71ea2-177a-4d2f-9a8f-c620c29d1766")]
    public readonly InputSlot<int> Index = new(0);
}