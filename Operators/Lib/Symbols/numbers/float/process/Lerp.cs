using T3.Core.Utils;

namespace Lib.numbers.@float.process;

[Guid("1f5294e1-9d0b-4cd5-9d65-14c8bbf59e61")]
internal sealed class Lerp : Instance<Lerp>
{
    [Output(Guid = "762ee8f9-52b5-4a0e-aa1e-b41fbb6d7d22")]
    public readonly Slot<float> Result = new();

    public Lerp()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var f = F.GetValue(context);
        if (Clamp.GetValue(context))
        {
            f = f.Clamp(0, 1);
        }
        Result.Value =  MathUtils.Lerp(A.GetValue(context), B.GetValue(context), f);
    }


    [Input(Guid = "f5904a0e-f7a6-4fd3-b8f3-8ecccd9363d7")]
    public readonly InputSlot<float> A = new();

    [Input(Guid = "1aed2c36-696a-4508-a8b6-3546ca570997")]
    public readonly InputSlot<float> B = new();
        
    [Input(Guid = "2B969154-EDF6-4FA6-972C-23D27E77E356")]
    public readonly InputSlot<float> F = new();
        
    [Input(Guid = "CC1A1668-795D-44B9-9BE0-23455C338AAF")]
    public readonly InputSlot<bool> Clamp = new();

        
}