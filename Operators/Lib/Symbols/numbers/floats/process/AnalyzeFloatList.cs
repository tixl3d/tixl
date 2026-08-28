using T3.Core.Utils;

namespace Lib.numbers.floats.process;

[Guid("9a46ab17-a523-4378-b118-54ab904e184a")]
internal sealed class AnalyzeFloatList : Instance<AnalyzeFloatList>
{
    [Output(Guid = "3E2AA291-0810-4F9B-9FEE-28088EF05C7F")]
    public readonly Slot<float> Min = new();
    
    [Output(Guid = "cab7bd9d-c6cb-4b81-872c-d99b91905c41")]
    public readonly Slot<float> Max = new();
    
    [Output(Guid = "71D700A8-17BC-40F5-9E72-F78C4E306660")]
    public readonly Slot<float> AverageMean = new();
    
    [Output(Guid = "6407243B-4614-4297-B3B7-12DD6A9630AF")]
    public readonly Slot<bool> AllValid = new();

    public AnalyzeFloatList()
    {
        Min.UpdateAction += Update;
        Max.UpdateAction += Update;
        AverageMean.UpdateAction += Update;
        AllValid.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (!Input.DirtyFlag.IsDirty)
        {
            return;
        }
        
        var list = Input.GetValue(context);
        if (list == null || list.Count == 0)
        {
            Min.Value = float.NaN;
            Max.Value = float.NaN;
            AverageMean.Value = float.NaN;
            AllValid.Value = false;
            return;
        }

        float sum = 0;
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        
        bool allValid = true;
        
        foreach (var v in list)
        {
            if (!float.IsFinite(v))
            {
                allValid = false;
                continue;
            }

            min = MathF.Min(min, v);
            max = MathF.Max(max, v);
            sum += v;
        }
        
        Min.Value = min;
        Max.Value = max;
        AverageMean.Value = sum / list.Count;
        AllValid.Value = allValid;
    }


    [Input(Guid = "54fa9e73-6669-4b98-b12e-8f6f06ac936f")]
    public readonly InputSlot<List<float>> Input = new();
}