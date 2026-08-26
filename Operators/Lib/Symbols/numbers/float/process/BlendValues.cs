using T3.Core.Utils;

namespace Lib.numbers.@float.process;

[Guid("a1341063-f893-4447-93cd-63cfe514495e")]
internal sealed class BlendValues : Instance<BlendValues>
{
    [Output(Guid = "b9e4e703-f797-4750-8a1d-d82c6953ea09")]
    public readonly Slot<float> Result = new();

    public BlendValues()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = 0;

        var collectedTypedInputs = Values.GetCollectedTypedInputs();
        var count = collectedTypedInputs.Count;
        if (count == 0)
            return;
            
        var f = F.GetValue(context);
            
        var index1 = (int)MathUtils.Fmod((int)f, count);
        var index2 = (int)MathUtils.Fmod((int)(f+1), count);
        var mix = MathUtils.Fmod(f, 1);

        Result.Value = MathUtils.Lerp(collectedTypedInputs[index1].GetValue(context),
                                      collectedTypedInputs[index2].GetValue(context),
                                      mix);
            
    }
        
         

    [Input(Guid = "1c15cce3-3054-47f8-96b5-b8b741df40be")]
    public readonly MultiInputSlot<float> Values = new();
        
    [Input(Guid = "03CE9DE0-2FD3-4900-84F7-2510F39DFF2A")]
    public readonly InputSlot<float> F = new();

}