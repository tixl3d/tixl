using T3.Core.Utils;

namespace Lib.numbers.@float.process;

[Guid("549ec76b-cc65-47b7-ad41-0abe93e86fe3")]
internal sealed class SmoothStep : Instance<SmoothStep>
{
    [Output(Guid = "7caa79cf-1a5a-42cc-b245-5937cd83e8dc")]
    public readonly Slot<float> Result = new();

    public SmoothStep()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = MathUtils.SmootherStep(Min.GetValue(context), Max.GetValue(context), Value.GetValue(context));
    }
        
    [Input(Guid = "2FC8855F-3AEF-42F1-9269-9390079FD348")]
    public readonly InputSlot<float> Min = new();

    [Input(Guid = "338E3B0B-4012-4C7D-899B-12B7384F5A80")]
    public readonly InputSlot<float> Max = new();
        
    [Input(Guid = "fe01e41c-bd3b-441c-82f6-6c972097e155")]
    public readonly InputSlot<float> Value = new();
        

}